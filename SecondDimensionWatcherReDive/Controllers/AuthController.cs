using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;
    private readonly IMemoryCache _memoryCache;

    private readonly TokenValidationParameters _tokenValidationParams;

    public AuthController(IConfiguration configuration, TokenValidationParameters tokenValidationParams,
        IMemoryCache memoryCache, ILogger<AuthController> logger)
    {
        _configuration = configuration;
        _tokenValidationParams = tokenValidationParams;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    private static string RandomString(int length)
    {
        var random = new Random();
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return new string(Enumerable.Repeat(chars, length)
            .Select(x => x[random.Next(x.Length)]).ToArray());
    }

    private LoginResult GenerateJwtToken()
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

        var refreshToken = new RefreshToken(RandomString(25) + Guid.NewGuid(), token.Id);

        _memoryCache.Set(refreshToken.Token, refreshToken);

        return new LoginResult(jwtToken, refreshToken.Token);
    }

    [HttpPost("register")]
    public async Task<ActionResult<LoginResult>> Register([FromBody] LoginData data)
    {
        if (!string.IsNullOrWhiteSpace(_configuration["Password:Value"]))
            return BadRequest();

        await System.IO.File.WriteAllBytesAsync("password.json",
            JsonSerializer.SerializeToUtf8Bytes(
                new PasswordConfig(new PasswordHash(BCrypt.Net.BCrypt.HashPassword(data.Password)))));

        return Ok(GenerateJwtToken());
    }

    [HttpPost("login")]
    public ActionResult<LoginResult> Login([FromBody] LoginData data)
    {
        var storedValue = _configuration["Password:Value"];
        if (string.IsNullOrWhiteSpace(storedValue))
            return BadRequest();

        if (!BCrypt.Net.BCrypt.Verify(data.Password, storedValue))
            return BadRequest();

        return Ok(GenerateJwtToken());
    }

    [HttpPost("refresh")]
    public ActionResult<LoginResult> Refresh([FromBody] AuthRequest request)
    {
        var result = VerifyAndGenerateToken(request);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    private LoginResult VerifyAndGenerateToken(AuthRequest request)
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
                return new LoginResult(null, null, false);

            var storedToken = _memoryCache.Get<RefreshToken>(request.RefreshToken);
            if (storedToken is null) return new LoginResult(null, null, false);

            if (tokenInVerification.FindFirst(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value != storedToken.JwtId)
                return new LoginResult(null, null, false);

            _memoryCache.Remove(request.RefreshToken);

            return GenerateJwtToken();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception.ToString());
            return new LoginResult(null, null, false);
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

    public record LoginData([Required] string Password);

    public record LoginResult(string? Token, string? RefreshToken, bool Success = true);

    public record AuthRequest([Required] string Token, [Required] string RefreshToken);

    public record RefreshToken(string Token, string JwtId);
}