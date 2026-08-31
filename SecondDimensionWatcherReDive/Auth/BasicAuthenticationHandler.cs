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
    private readonly BasicAuthenticationAttemptLimiter _attemptLimiter;

    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IDeviceTokenHasher tokenHasher,
        IMemoryCache verificationCache,
        BasicAuthenticationAttemptLimiter attemptLimiter) : base(options, logger, encoder)
    {
        _tokenHasher = tokenHasher;
        _verificationCache = verificationCache;
        _attemptLimiter = attemptLimiter;
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
            return await RejectAuthenticationAttemptAsync("Malformed Basic credentials.");
        }

        var separator = decoded.IndexOf(':');
        if (separator < 0)
            return await RejectAuthenticationAttemptAsync("Malformed Basic credentials.");

        var username = decoded[..separator];
        var password = decoded[(separator + 1)..];

        var repository = Context.RequestServices.GetRequiredService<IWebDavTokenRepository>();
        var record = await repository.FindByUsernameAsync(username, Context.RequestAborted);
        if (record is null)
            return await RejectAuthenticationAttemptAsync("Invalid credentials.");

        bool verified;
        var attemptAlreadyCounted = false;
        if (_tokenHasher.IsModernHash(record.TokenHash))
        {
            verified = _tokenHasher.Verify(password, record.TokenHash);
        }
        else
        {
            var cacheKey = _tokenHasher.VerificationCacheKey(record.Id, password);
            if (!_verificationCache.TryGetValue(cacheKey, out verified))
            {
                if (!await TryConsumeAuthenticationAttemptAsync())
                    return AuthenticateResult.Fail("Too many Basic authentication attempts.");
                attemptAlreadyCounted = true;

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
                    _verificationCache.Set(cacheKey, true, TimeSpan.FromMinutes(2));
                }
            }
        }

        if (!verified)
            return attemptAlreadyCounted
                ? AuthenticateResult.Fail("Invalid credentials.")
                : await RejectAuthenticationAttemptAsync("Invalid credentials.");

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, username)], Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items.ContainsKey(BasicAuthenticationAttemptLimiter.RateLimitedItemKey))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return Task.CompletedTask;
        }

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"Basic realm=\"{Realm}\", charset=\"UTF-8\"";
        return Task.CompletedTask;
    }

    private async Task<AuthenticateResult> RejectAuthenticationAttemptAsync(string message)
        => await TryConsumeAuthenticationAttemptAsync()
            ? AuthenticateResult.Fail(message)
            : AuthenticateResult.Fail("Too many Basic authentication attempts.");

    private async Task<bool> TryConsumeAuthenticationAttemptAsync()
    {
        var result = await _attemptLimiter.AttemptAcquireAsync(
            Context.Connection.RemoteIpAddress,
            Context.RequestAborted);
        if (result.IsAcquired)
            return true;

        Context.Items[BasicAuthenticationAttemptLimiter.RateLimitedItemKey] = true;
        // VFS accepts either Basic or Bearer. A later Bearer challenge may set 401
        // after this handler runs, so enforce the terminal status at response start.
        Response.OnStarting(static state =>
        {
            var context = (HttpContext)state;
            if (context.Items.ContainsKey(BasicAuthenticationAttemptLimiter.RateLimitedItemKey))
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return Task.CompletedTask;
        }, Context);
        if (result.RetryAfter is { } retryAfter)
        {
            Response.Headers.RetryAfter = Math.Max(
                1,
                (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }
        return false;
    }
}
