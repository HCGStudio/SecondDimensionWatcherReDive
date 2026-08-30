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

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("auth")]
internal partial class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;
    private readonly RefreshTokenStore _refreshTokens;
    private readonly TokenValidationParameters _tokenValidationParams;
    private readonly TokenSecurityOptions _securityOptions;
    private readonly TimeProvider _timeProvider;

    public AuthController(
        IConfiguration configuration,
        TokenValidationParameters tokenValidationParams,
        RefreshTokenStore refreshTokens,
        IOptions<TokenSecurityOptions> securityOptions,
        TimeProvider timeProvider,
        ILogger<AuthController> logger)
    {
        _configuration = configuration;
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

        var passwordFile = _configuration["PasswordFile"] ?? "password.json";
        await System.IO.File.WriteAllBytesAsync(passwordFile,
            JsonSerializer.SerializeToUtf8Bytes(
                new External.PasswordConfig(new External.PasswordHash(BCrypt.Net.BCrypt.HashPassword(data.Password))),
                External.AppJsonSerializerContext.Default.PasswordConfig),
            cancellationToken);

        return Ok(await GenerateJwtTokenAsync(cancellationToken));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] External.LoginData data,
        CancellationToken cancellationToken)
    {
        var storedValue = _configuration["Password:Value"];
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
    public async Task<IActionResult> Logout(
        [FromBody] External.RevokeTokenRequest request,
        CancellationToken cancellationToken)
    {
        await _refreshTokens.RevokeAsync(request.RefreshToken, cancellationToken);
        return NoContent();
    }

    [HttpGet("verify")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public IActionResult Verify()
    {
        return Ok(HttpContext.User.Claims.Select(c => new { c.Type, c.Value }));
    }

    [HttpGet("allowRegister")]
    public IActionResult CanRegister()
    {
        return Ok(new { Allow = string.IsNullOrWhiteSpace(_configuration["Password:Value"]) });
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
}
