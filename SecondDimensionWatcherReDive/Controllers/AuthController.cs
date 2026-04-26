using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
internal partial class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;
    private readonly IDistributedCache _distributedCache;

    private readonly TokenValidationParameters _tokenValidationParams;

    public AuthController(IConfiguration configuration, TokenValidationParameters tokenValidationParams,
        IDistributedCache distributedCache, ILogger<AuthController> logger)
    {
        _configuration = configuration;
        _tokenValidationParams = tokenValidationParams;
        _distributedCache = distributedCache;
        _logger = logger;
    }

    private static string RandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return RandomNumberGenerator.GetString(chars, length);
    }

    private async Task<External.LoginResult> GenerateJwtTokenAsync()
    {
        var handler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_configuration["JwtSecret"]!);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("Id", Guid.Empty.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            }),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials =
                new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = handler.CreateToken(tokenDescriptor);
        var jwtToken = handler.WriteToken(token);

        var refreshToken = new External.RefreshToken(RandomString(25) + Guid.NewGuid(), token.Id);

        await _distributedCache.SetStringAsync(refreshToken.Token,
            JsonSerializer.Serialize(refreshToken, External.AppJsonSerializerContext.Default.RefreshToken));

        return new External.LoginResult(jwtToken, refreshToken.Token);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] External.LoginData data)
    {
        if (!string.IsNullOrWhiteSpace(_configuration["Password:Value"]))
            return BadRequest();

        var passwordFile = _configuration["PasswordFile"] ?? "password.json";
        await System.IO.File.WriteAllBytesAsync(passwordFile,
            JsonSerializer.SerializeToUtf8Bytes(
                new External.PasswordConfig(new External.PasswordHash(BCrypt.Net.BCrypt.HashPassword(data.Password))),
                External.AppJsonSerializerContext.Default.PasswordConfig));

        return Ok(await GenerateJwtTokenAsync());
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] External.LoginData data)
    {
        var storedValue = _configuration["Password:Value"];
        if (string.IsNullOrWhiteSpace(storedValue))
            return BadRequest();

        if (!BCrypt.Net.BCrypt.Verify(data.Password, storedValue))
            return BadRequest();

        return Ok(await GenerateJwtTokenAsync());
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] External.AuthRequest request)
    {
        var result = await VerifyAndGenerateTokenAsync(request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    private async Task<External.LoginResult> VerifyAndGenerateTokenAsync(External.AuthRequest request)
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

            var storedJson = await _distributedCache.GetStringAsync(request.RefreshToken);
            var storedToken = storedJson is null ? null : JsonSerializer.Deserialize(storedJson, External.AppJsonSerializerContext.Default.RefreshToken);
            if (storedToken is null) return new External.LoginResult(null, null, false);

            if (tokenInVerification.FindFirst(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value != storedToken.JwtId)
                return new External.LoginResult(null, null, false);

            await _distributedCache.RemoveAsync(request.RefreshToken);

            return await GenerateJwtTokenAsync();
        }
        catch (Exception exception)
        {
            LogTokenVerificationFailed(_logger, exception);
            return new External.LoginResult(null, null, false);
        }
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Token verification failed")]
    private static partial void LogTokenVerificationFailed(ILogger logger, Exception ex);
}
