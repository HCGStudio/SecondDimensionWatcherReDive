using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/webdav-tokens")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal partial class WebDavTokenController(IWebDavTokenRepository repository) : ControllerBase
{
    private const string UsernameAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const int GeneratedUsernameLength = 8;
    private const int TokenByteLength = 32;

    [GeneratedRegex(@"^[A-Za-z0-9._-]{3,32}$")]
    private static partial Regex UsernamePattern();

    [HttpGet]
    public async Task<IActionResult> ListTokens(CancellationToken cancellationToken)
    {
        var records = await repository.GetAllOrderedAsync(cancellationToken);
        return Ok(records.Select(r => r.ToExternal()).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> CreateToken(
        [FromBody] External.CreateWebDavTokenRequest request,
        CancellationToken cancellationToken)
    {
        var requested = request.Username?.Trim();
        var username = string.IsNullOrEmpty(requested)
            ? GenerateUsername()
            : requested;

        if (!UsernamePattern().IsMatch(username))
            return BadRequest(new { error = "Username must be 3-32 characters of letters, digits, '.', '_' or '-'." });

        if (await repository.ExistsByUsernameAsync(username, cancellationToken))
            return Conflict(new { error = "Username already exists." });

        var plaintext = GenerateToken();
        var hash = BCrypt.Net.BCrypt.HashPassword(plaintext);
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var record = new WebDavToken(Guid.NewGuid(), username, hash, description, DateTimeOffset.UtcNow);

        await repository.AddAsync(record, cancellationToken);

        return Ok(new External.CreateWebDavTokenResponse(
            record.Id,
            record.Username,
            plaintext,
            record.Description,
            record.CreatedAt));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteToken([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var removed = await repository.RemoveByIdAsync(id, cancellationToken);
        return removed ? NoContent() : NotFound();
    }

    private static string GenerateUsername() =>
        "sdw-" + RandomNumberGenerator.GetString(UsernameAlphabet, GeneratedUsernameLength);

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
