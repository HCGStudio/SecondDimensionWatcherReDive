using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Configuration;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("auth")]
internal partial class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IAuthenticationStateRepository _authenticationStateRepository;
    private readonly ILogger<AuthController> _logger;
    private readonly RefreshTokenStore _refreshTokens;
    private readonly TokenValidationParameters _tokenValidationParams;
    private readonly TokenSecurityOptions _securityOptions;
    private readonly TimeProvider _timeProvider;

    public AuthController(
        IConfiguration configuration,
        IAuthenticationStateRepository authenticationStateRepository,
        TokenValidationParameters tokenValidationParams,
        RefreshTokenStore refreshTokens,
        IOptions<TokenSecurityOptions> securityOptions,
        TimeProvider timeProvider,
        ILogger<AuthController> logger)
    {
        _configuration = configuration;
        _authenticationStateRepository = authenticationStateRepository;
        _tokenValidationParams = tokenValidationParams;
        _refreshTokens = refreshTokens;
        _securityOptions = securityOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private async Task<External.LoginResult> GenerateJwtTokenAsync(CancellationToken cancellationToken)
    {
        var jwtId = Guid.NewGuid().ToString();
        var refreshToken = await _refreshTokens.IssueAsync(
            jwtId,
            cancellationToken);
        return refreshToken is null
            ? new External.LoginResult(null, null, false)
            : CreateLoginResult(refreshToken);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] External.LoginData data,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_configuration["Password:Value"]))
            return BadRequest();
        if (!string.IsNullOrWhiteSpace(
                await _authenticationStateRepository.GetPasswordHashAsync(cancellationToken)))
            return BadRequest();

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(data.Password);
        if (!await _authenticationStateRepository.TryClaimPasswordAsync(
                passwordHash,
                Guid.NewGuid(),
                _timeProvider.GetUtcNow(),
                cancellationToken))
            return BadRequest();

        var passwordFile = _configuration["PasswordFile"] ?? "password.json";
        try
        {
            await PersistPasswordFileAsync(passwordFile, passwordHash, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // PostgreSQL is authoritative. A failed compatibility-file update must not strand
            // the sole successful claimant without its access/refresh credentials.
            LogPasswordFilePersistenceFailed(_logger, exception);
        }

        return Ok(await GenerateJwtTokenAsync(cancellationToken));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] External.LoginData data,
        CancellationToken cancellationToken)
    {
        var storedValue = await GetPasswordHashAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(storedValue))
            return BadRequest();

        if (!BCrypt.Net.BCrypt.Verify(data.Password, storedValue))
            return BadRequest();

        return Ok(await GenerateJwtTokenAsync(cancellationToken));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        [FromBody] External.AuthRequest request,
        CancellationToken cancellationToken)
    {
        var result = await VerifyAndGenerateTokenAsync(request, cancellationToken);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    private async Task<External.LoginResult> VerifyAndGenerateTokenAsync(
        External.AuthRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var param = _tokenValidationParams.Clone();
            param.ValidateLifetime = false;
            var tokenInVerification =
                handler.ValidateToken(request.Token, param, out var validatedToken);


            if (validatedToken is JwtSecurityToken securityToken && !securityToken.Header.Alg.Equals(
                    SecurityAlgorithms.HmacSha256,
                    StringComparison.InvariantCultureIgnoreCase))
                return new External.LoginResult(null, null, false);

            var jwtId = tokenInVerification.FindFirst(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;
            if (string.IsNullOrEmpty(jwtId))
                return new External.LoginResult(null, null, false);

            var replacement = await _refreshTokens.RotateAsync(
                request.RefreshToken,
                jwtId,
                Guid.NewGuid().ToString(),
                cancellationToken);
            return replacement is null
                ? new External.LoginResult(null, null, false)
                : CreateLoginResult(replacement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogTokenVerificationFailed(_logger, exception);
            return new External.LoginResult(null, null, false);
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [DisableRateLimiting]
    public async Task<IActionResult> Logout(
        [FromBody] External.RevokeTokenRequest request,
        CancellationToken cancellationToken)
    {
        await _refreshTokens.RevokeAsync(request.RefreshToken, cancellationToken);
        Response.Cookies.Delete(PlaybackTicketService.SecureCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
        Response.Cookies.Delete(PlaybackTicketService.DevelopmentCookieName, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/file/play"
        });
        return NoContent();
    }

    [HttpGet("verify")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public IActionResult Verify()
    {
        return Ok(HttpContext.User.Claims.Select(c => new { c.Type, c.Value }));
    }

    [HttpGet("allowRegister")]
    public async Task<IActionResult> CanRegister(CancellationToken cancellationToken)
    {
        return Ok(new
        {
            Allow = string.IsNullOrWhiteSpace(await GetPasswordHashAsync(cancellationToken))
        });
    }

    private async Task<string?> GetPasswordHashAsync(CancellationToken cancellationToken)
    {
        var databaseHash = await _authenticationStateRepository.GetPasswordHashAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(databaseHash))
            return databaseHash;

        var deploymentHash = _configuration["Password:Value"];
        return string.IsNullOrWhiteSpace(deploymentHash) ? null : deploymentHash;
    }

    private static async Task PersistPasswordFileAsync(
        string passwordFile,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(passwordFile);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        var contents = JsonSerializer.SerializeToUtf8Bytes(
            new External.PasswordConfig(new External.PasswordHash(passwordHash)),
            External.AppJsonSerializerContext.Default.PasswordConfig);
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        try
        {
            await using (var stream = new FileStream(temporaryPath, options))
            {
                await stream.WriteAsync(contents, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            System.IO.File.Move(temporaryPath, fullPath, overwrite: true);
            if (!OperatingSystem.IsWindows())
                System.IO.File.SetUnixFileMode(fullPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            System.IO.File.Delete(temporaryPath);
        }
    }

    private External.LoginResult CreateLoginResult(IssuedRefreshToken refreshToken)
    {
        var handler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_configuration["JwtSecret"]!);
        var now = _timeProvider.GetUtcNow();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("Id", Guid.Empty.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, refreshToken.JwtId)
            }),
            Issuer = _securityOptions.Issuer,
            Audience = _securityOptions.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(_securityOptions.AccessTokenMinutes).UtcDateTime,
            SigningCredentials =
                new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = handler.CreateToken(tokenDescriptor);
        return new External.LoginResult(handler.WriteToken(token), refreshToken.Token);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Token verification failed")]
    private static partial void LogTokenVerificationFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "The durable password claim succeeded, but the compatibility password file could not be updated")]
    private static partial void LogPasswordFilePersistenceFailed(ILogger logger, Exception exception);
}
