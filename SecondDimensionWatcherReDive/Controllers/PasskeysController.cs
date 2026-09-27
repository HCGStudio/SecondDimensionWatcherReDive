using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Controllers.External;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/auth/passkeys")]
[EnableRateLimiting("auth")]
[RequestSizeLimit(65536)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
internal sealed class PasskeysController(
    IPasskeyRepository passkeys,
    IIdentityRepository identities,
    SessionTokenIssuer tokenIssuer,
    IAuthenticationStateRepository authenticationState) : ControllerBase
{
    private const string BindingCookie = "__Host-sdw-passkey";
    private const string Registration = "registration";
    private const string LoginPurpose = "login";
    private const string Reauthentication = "reauthenticate";
    private const string PasswordRemoval = "password-removal";

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet]
    public async Task<IActionResult> Settings(CancellationToken cancellationToken)
    {
        if (TransportError() is { } error) return error;
        var session = await CurrentSessionAsync(cancellationToken);
        if (session is null) return Unauthorized();
        var credentials = await passkeys.GetCredentialsAsync(session.User.Id, RelyingPartyId, cancellationToken);
        var hasPassword = !session.User.PasswordRemoved && (session.User.PasswordHash is not null
            || session.User.Id == IdentityDefaults.UserId
            && !string.IsNullOrWhiteSpace(await authenticationState.GetPasswordHashAsync(cancellationToken)));
        return Ok(new PasskeySettingsResponse(hasPassword, credentials.Select(value => new PasskeySummary(
            value.Id, value.Name, value.CreatedAt, value.LastUsedAt)).ToList()));
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("registration/options")]
    public async Task<IActionResult> RegistrationOptions(CancellationToken cancellationToken)
    {
        if (TransportError() is { } error) return error;
        var session = await CurrentSessionAsync(cancellationToken);
        if (session is null) return Unauthorized();
        if (!IsRecent(session)) return Failure(403, "reauthenticationRequired");
        var credentials = await passkeys.GetCredentialsAsync(session.User.Id, null, cancellationToken);
        if (credentials.Count >= 20) return Failure(409, "passkeyLimitReached");
        var options = Verifier(Origin, RelyingPartyId).RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User
            {
                Id = UserHandle(session.User.Id), Name = session.User.Username, DisplayName = session.User.Username
            },
            ExcludeCredentials = credentials.Where(value => value.RelyingPartyId == RelyingPartyId)
                .Select(value => new PublicKeyCredentialDescriptor(value.CredentialId)).ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required,
                UserVerification = UserVerificationRequirement.Required
            },
            AttestationPreference = AttestationConveyancePreference.None,
            PubKeyCredParams = [PubKeyCredParam.ES256, PubKeyCredParam.RS256]
        });
        return await SaveOptionsAsync(Registration, session.User.Id, session.Session.Id,
            options.ToJson(), cancellationToken);
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("registration")]
    public async Task<IActionResult> Register(PasskeyRegistrationRequest request, CancellationToken cancellationToken)
    {
        if (TransportError() is { } error) return error;
        var session = await CurrentSessionAsync(cancellationToken);
        if (session is null) return Unauthorized();
        if (!IsRecent(session)) return Failure(403, "reauthenticationRequired");
        var ceremony = await ConsumeAsync(request.CeremonyId, Registration, session.Session.Id, cancellationToken);
        if (ceremony is null || ceremony.UserId != session.User.Id) return Failure(400, "invalidCeremony");
        try
        {
            var response = request.Credential;
            if (string.IsNullOrWhiteSpace(request.Name) || response.RawId is not { Length: > 0 and <= 1024 }
                || response.Response is null || response.Response.AttestationObject is not { Length: > 0 }
                || !ValidClientData(response.Response.ClientDataJson, ceremony.Origin))
                return Failure(400, "invalidCredential");
            var credential = await Verifier(ceremony.Origin, ceremony.RelyingPartyId)
                .MakeNewCredentialAsync(new MakeNewCredentialParams
                {
                    AttestationResponse = response,
                    OriginalOptions = CredentialCreateOptions.FromJson(ceremony.OptionsJson),
                    IsCredentialIdUniqueToUserCallback = async (args, token) =>
                        await passkeys.FindCredentialAsync(args.CredentialId, token) is null
                }, cancellationToken);
            if (credential.Id.Length > 1024) return Failure(400, "invalidCredential");
            var stored = new PasskeyCredential(Guid.NewGuid(), session.User.Id, credential.Id,
                credential.PublicKey, credential.SignCount, ceremony.RelyingPartyId, request.Name.Trim(),
                credential.IsBackupEligible, credential.IsBackedUp, DateTimeOffset.UtcNow, null);
            return await passkeys.AddCredentialAsync(stored, session.Session.Id, DateTimeOffset.UtcNow, cancellationToken)
                ? NoContent() : Failure(409, "passkeyRegistrationRejected");
        }
        catch (Exception exception) when (IsInvalidCredential(exception))
        {
            return Failure(400, "invalidCredential");
        }
    }

    [HttpPost("login/options")]
    public async Task<IActionResult> LoginOptions(PasskeyLoginOptionsRequest request, CancellationToken cancellationToken)
    {
        if (TransportError() is { } error) return error;
        var username = string.IsNullOrWhiteSpace(request.Username) ? IdentityDefaults.Username : request.Username.Trim();
        var user = await identities.FindUserByUsernameAsync(username, cancellationToken);
        return await AssertionOptionsAsync(LoginPurpose, user is { IsDisabled: false } ? user.Id : null,
            null, cancellationToken);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(PasskeyAssertionRequest request, CancellationToken cancellationToken)
    {
        if (TransportError() is { } error) return error;
        var result = await VerifyAssertionAsync(request, LoginPurpose, null, cancellationToken);
        if (result is null) return Failure(401, "invalidCredential");
        var user = await identities.FindUserByIdAsync(result.UserId, cancellationToken);
        if (user is null || user.IsDisabled) return Unauthorized();
        var profiles = await identities.GetProfilesAsync(user.Id, cancellationToken);
        var profile = profiles.FirstOrDefault(value => value.IsDefault) ?? profiles.FirstOrDefault();
        if (profile is null) return Unauthorized();
        return Ok(ToResult(await tokenIssuer.CreateSessionAsync(user, profile, request.DeviceName, cancellationToken)));
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("reauthenticate/options")]
    public Task<IActionResult> ReauthenticationOptions(CancellationToken cancellationToken) =>
        SessionAssertionOptionsAsync(Reauthentication, cancellationToken);

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("password-removal/options")]
    public Task<IActionResult> PasswordRemovalOptions(CancellationToken cancellationToken) =>
        SessionAssertionOptionsAsync(PasswordRemoval, cancellationToken);

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("reauthenticate")]
    public async Task<IActionResult> Reauthenticate(PasskeyAssertionRequest request, CancellationToken cancellationToken)
    {
        if (TransportError() is { } error) return error;
        var session = await CurrentSessionAsync(cancellationToken);
        if (session is null || string.IsNullOrEmpty(request.RefreshToken)) return Unauthorized();
        var credential = await VerifyAssertionAsync(request, Reauthentication, session.Session.Id, cancellationToken);
        if (credential is null || credential.UserId != session.User.Id) return Failure(400, "invalidCredential");
        var tokens = await tokenIssuer.RotateSessionAsync(session, session.Profile,
            request.RefreshToken, reauthenticated: true, cancellationToken);
        return tokens is null ? Unauthorized() : Ok(ToResult(tokens));
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("password-removal")]
    public async Task<IActionResult> RemovePassword(PasskeyAssertionRequest request, CancellationToken cancellationToken)
    {
        if (TransportError() is { } error) return error;
        var session = await CurrentSessionAsync(cancellationToken);
        if (session is null) return Unauthorized();
        var credential = await VerifyAssertionAsync(request, PasswordRemoval, session.Session.Id, cancellationToken);
        if (credential is null || credential.UserId != session.User.Id) return Failure(400, "invalidCredential");
        return await passkeys.RemovePasswordAsync(session.User.Id, session.Session.Id,
            credential.Id, DateTimeOffset.UtcNow, cancellationToken) ? NoContent() : Unauthorized();
    }

    private async Task<IActionResult> SessionAssertionOptionsAsync(string purpose, CancellationToken cancellationToken)
    {
        if (TransportError() is { } error) return error;
        var session = await CurrentSessionAsync(cancellationToken);
        if (session is null) return Unauthorized();
        return await AssertionOptionsAsync(purpose, session.User.Id, session.Session.Id, cancellationToken);
    }

    private async Task<IActionResult> AssertionOptionsAsync(string purpose, Guid? userId, Guid? sessionId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PasskeyCredential> credentials = userId.HasValue
            ? await passkeys.GetCredentialsAsync(userId.Value, RelyingPartyId, cancellationToken) : [];
        if (credentials.Count == 0 && purpose != LoginPurpose) return Failure(409, "passkeyRequired");
        var options = Verifier(Origin, RelyingPartyId).GetAssertionOptions(new GetAssertionOptionsParams
        {
            // An unknown account gets an unusable allow-list instead of a discoverable credential prompt.
            AllowedCredentials = credentials.Count == 0
                ? [new PublicKeyCredentialDescriptor(RandomNumberGenerator.GetBytes(32))]
                : credentials.Select(value => new PublicKeyCredentialDescriptor(value.CredentialId)).ToList(),
            UserVerification = UserVerificationRequirement.Required
        });
        return await SaveOptionsAsync(purpose, userId, sessionId, options.ToJson(), cancellationToken);
    }

    private async Task<IActionResult> SaveOptionsAsync(string purpose, Guid? userId, Guid? sessionId,
        string optionsJson, CancellationToken cancellationToken)
    {
        var binding = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        var ceremony = new PasskeyCeremony(Guid.NewGuid(), userId, sessionId, purpose, Origin,
            RelyingPartyId, HashBinding(binding), optionsJson, now.AddMinutes(5));
        await passkeys.AddCeremonyAsync(ceremony, now, cancellationToken);
        Response.Cookies.Append(BindingCookie, binding, new CookieOptions
        {
            Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/",
            MaxAge = TimeSpan.FromMinutes(5), IsEssential = true
        });
        using var options = JsonDocument.Parse(optionsJson);
        return Ok(new PasskeyOptionsResponse(ceremony.Id, options.RootElement.Clone()));
    }

    private async Task<PasskeyCeremony?> ConsumeAsync(Guid id, string purpose, Guid? sessionId,
        CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(BindingCookie, out var binding) || binding.Length != 64) return null;
        return await passkeys.ConsumeCeremonyAsync(id, purpose, sessionId, Origin,
            HashBinding(binding), DateTimeOffset.UtcNow, cancellationToken);
    }

    private async Task<PasskeyCredential?> VerifyAssertionAsync(PasskeyAssertionRequest request,
        string purpose, Guid? sessionId, CancellationToken cancellationToken)
    {
        var ceremony = await ConsumeAsync(request.CeremonyId, purpose, sessionId, cancellationToken);
        if (ceremony?.UserId is not { } userId) return null;
        try
        {
            var response = request.Credential;
            if (response.RawId is not { Length: > 0 and <= 1024 } || response.Response is null
                || response.Response.AuthenticatorData is not { Length: >= 37 }
                || response.Response.Signature is not { Length: > 0 }
                || !ValidClientData(response.Response.ClientDataJson, ceremony.Origin)) return null;
            var credential = await passkeys.FindCredentialAsync(response.RawId, cancellationToken);
            if (credential is null || credential.UserId != userId
                || credential.RelyingPartyId != ceremony.RelyingPartyId
                || credential.IsBackupEligible != ((response.Response.AuthenticatorData[32] & 0x08) != 0)) return null;
            var verified = await Verifier(ceremony.Origin, ceremony.RelyingPartyId).MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = response,
                OriginalOptions = Fido2NetLib.AssertionOptions.FromJson(ceremony.OptionsJson),
                StoredPublicKey = credential.PublicKey,
                StoredSignatureCounter = checked((uint)credential.SignCount),
                IsUserHandleOwnerOfCredentialIdCallback = (args, _) => Task.FromResult(
                    args.UserHandle.AsSpan().SequenceEqual(UserHandle(userId))
                    && args.CredentialId.AsSpan().SequenceEqual(credential.CredentialId))
            }, cancellationToken);
            return await passkeys.RecordAssertionAsync(credential, verified.SignCount,
                verified.IsBackedUp, DateTimeOffset.UtcNow, cancellationToken) ? credential : null;
        }
        catch (Exception exception) when (IsInvalidCredential(exception))
        {
            return null;
        }
    }

    private async Task<AuthenticatedSession?> CurrentSessionAsync(CancellationToken cancellationToken) =>
        User.TryGetSessionId(out var id)
            ? await identities.GetAuthenticatedSessionAsync(id, DateTimeOffset.UtcNow, cancellationToken) : null;

    // Bind the verifier to the HTTPS request authority, never to an Origin supplied by the client.
    // TrustedProxyConfiguration restores HTTPS only for explicitly trusted proxies.
    private string Origin => new Uri($"https://{Request.Host.Value}").GetLeftPart(UriPartial.Authority);
    private string RelyingPartyId => new Uri(Origin).IdnHost;
    private IActionResult? TransportError()
    {
        if (!Request.IsHttps) return Failure(403, "httpsRequired");
        if (!Request.Host.HasValue || !Uri.TryCreate($"https://{Request.Host.Value}", UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/"
            || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.IdnHost.Length > 253)
            return Failure(400, "invalidOrigin");
        if (Request.Headers.TryGetValue("Origin", out var origin)
            && !string.Equals(origin.ToString(), Origin, StringComparison.Ordinal)) return Failure(400, "invalidOrigin");
        return null;
    }

    private static Fido2 Verifier(string origin, string relyingPartyId) => new(new Fido2Configuration
    {
        RPID = relyingPartyId, RPName = "Second Dimension Watcher", Origins = new HashSet<string> { origin },
        ChallengeSize = 32, Timeout = 300000
    });

    private static bool ValidClientData(byte[]? data, string origin)
    {
        if (data is not { Length: > 0 }) return false;
        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("origin", out var value) && value.ValueKind == JsonValueKind.String
            && string.Equals(value.GetString(), origin, StringComparison.Ordinal)
            && (!root.TryGetProperty("crossOrigin", out var crossOrigin) || crossOrigin.ValueKind == JsonValueKind.False)
            && !root.TryGetProperty("topOrigin", out _);
    }

    private static bool IsInvalidCredential(Exception exception) => exception is Fido2VerificationException
        or JsonException or ArgumentException or FormatException or CborContentException or CryptographicException;
    private static bool IsRecent(AuthenticatedSession session) =>
        session.Session.AuthenticatedAt >= DateTimeOffset.UtcNow.AddMinutes(-5);
    private static byte[] UserHandle(Guid userId) => Encoding.UTF8.GetBytes(userId.ToString("N"));
    private static string HashBinding(string binding) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(binding)));
    private ObjectResult Failure(int status, string code) => StatusCode(status, new CodedMessageResponse(code, code));
    private static LoginResult ToResult(IssuedSessionTokens tokens) =>
        new(tokens.AccessToken, tokens.RefreshToken, true, tokens.SessionId, tokens.ProfileId);
}
