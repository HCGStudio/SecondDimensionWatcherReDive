using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Auth;

internal sealed class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Basic";
    private const string Realm = "SecondDimensionWatcher WebDAV";

    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
            return AuthenticateResult.NoResult();

        if (!AuthenticationHeaderValue.TryParse(headerValues.ToString(), out var header) ||
            !string.Equals(header.Scheme, SchemeName, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(header.Parameter))
            return AuthenticateResult.NoResult();

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch (FormatException)
        {
            return AuthenticateResult.Fail("Malformed Basic credentials.");
        }

        var separator = decoded.IndexOf(':');
        if (separator < 0)
            return AuthenticateResult.Fail("Malformed Basic credentials.");

        var username = decoded[..separator];
        var password = decoded[(separator + 1)..];

        var repository = Context.RequestServices.GetRequiredService<IWebDavTokenRepository>();
        var record = await repository.FindByUsernameAsync(username, Context.RequestAborted);
        var now = DateTimeOffset.UtcNow;
        if (record is null
            || record.RevokedAt is not null
            || record.ExpiresAt is { } expiresAt && expiresAt <= now
            || !string.Equals(record.Scope, "read", StringComparison.Ordinal)
            || !DevicePathScope.TryNormalizeAbsolutePath(record.VirtualRoot, out var virtualRoot))
            return AuthenticateResult.Fail("Invalid credentials.");

        var identityRepository = Context.RequestServices.GetRequiredService<IIdentityRepository>();
        var user = await identityRepository.FindUserByIdAsync(record.UserId, Context.RequestAborted);
        if (user is null || user.IsDisabled)
            return AuthenticateResult.Fail("Invalid credentials.");

        bool verified;
        try
        {
            verified = BCrypt.Net.BCrypt.Verify(password, record.TokenHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return AuthenticateResult.Fail("Stored token hash is invalid.");
        }

        if (!verified)
            return AuthenticateResult.Fail("Invalid credentials.");

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(IdentityClaimTypes.UserId, user.Id.ToString()),
            new Claim(IdentityClaimTypes.DeviceTokenId, record.Id.ToString()),
            new Claim(IdentityClaimTypes.DeviceScope, record.Scope),
            new Claim(IdentityClaimTypes.VirtualRoot, virtualRoot)
        ], Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"Basic realm=\"{Realm}\", charset=\"UTF-8\"";
        return Task.CompletedTask;
    }
}
