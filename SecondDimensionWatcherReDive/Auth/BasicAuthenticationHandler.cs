using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SecondDimensionWatcherReDive.Auth;

internal sealed class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Basic";
    private const string FixedUserName = "sdwuser";
    private const string Realm = "SecondDimensionWatcher WebDAV";

    private readonly IConfiguration _configuration;

    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration) : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!AuthenticationHeaderValue.TryParse(headerValues.ToString(), out var header) ||
            !string.Equals(header.Scheme, SchemeName, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(header.Parameter))
            return Task.FromResult(AuthenticateResult.NoResult());

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch (FormatException)
        {
            return Task.FromResult(AuthenticateResult.Fail("Malformed Basic credentials."));
        }

        var separator = decoded.IndexOf(':');
        if (separator < 0)
            return Task.FromResult(AuthenticateResult.Fail("Malformed Basic credentials."));

        var username = decoded[..separator];
        var password = decoded[(separator + 1)..];

        if (!string.Equals(username, FixedUserName, StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.Fail("Invalid credentials."));

        var storedHash = _configuration["Password:Value"];
        if (string.IsNullOrWhiteSpace(storedHash))
            return Task.FromResult(AuthenticateResult.Fail("Password not configured."));

        bool verified;
        try
        {
            verified = BCrypt.Net.BCrypt.Verify(password, storedHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return Task.FromResult(AuthenticateResult.Fail("Stored password hash is invalid."));
        }

        if (!verified)
            return Task.FromResult(AuthenticateResult.Fail("Invalid credentials."));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, FixedUserName)], Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"Basic realm=\"{Realm}\", charset=\"UTF-8\"";
        return Task.CompletedTask;
    }
}
