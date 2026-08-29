using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Auth;

internal sealed class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Basic";
    private const string Realm = "SecondDimensionWatcher WebDAV";
    private readonly IDeviceTokenHasher _tokenHasher;
    private readonly IMemoryCache _verificationCache;

    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IDeviceTokenHasher tokenHasher,
        IMemoryCache verificationCache) : base(options, logger, encoder)
    {
        _tokenHasher = tokenHasher;
        _verificationCache = verificationCache;
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
        if (record is null)
            return AuthenticateResult.Fail("Invalid credentials.");

        bool verified;
        if (_tokenHasher.IsModernHash(record.TokenHash))
        {
            verified = _tokenHasher.Verify(password, record.TokenHash);
        }
        else
        {
            var cacheKey = _tokenHasher.VerificationCacheKey(record.Id, password);
            if (!_verificationCache.TryGetValue(cacheKey, out verified))
            {
                try
                {
                    verified = BCrypt.Net.BCrypt.Verify(password, record.TokenHash);
                }
                catch (BCrypt.Net.SaltParseException)
                {
                    return AuthenticateResult.Fail("Stored token hash is invalid.");
                }

                if (verified)
                {
                    await repository.UpdateHashAsync(
                        record.Id,
                        record.TokenHash,
                        _tokenHasher.Hash(password),
                        Context.RequestAborted);
                }
                _verificationCache.Set(cacheKey, verified, TimeSpan.FromMinutes(2));
            }
        }

        if (!verified)
            return AuthenticateResult.Fail("Invalid credentials.");

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, username)], Scheme.Name);
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
