using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/webdav-tokens")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal partial class WebDavTokenController(
    IWebDavTokenRepository repository,
    IIdentityRepository identityRepository,
    IFileMappingRepository fileMappingRepository) : ControllerBase
{
    private const string UsernameAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const int GeneratedUsernameLength = 8;
    private const int TokenByteLength = 32;
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(365);
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromDays(365 * 5);

    [GeneratedRegex(@"^[A-Za-z0-9._-]{3,32}$")]
    private static partial Regex UsernamePattern();

    [HttpGet]
    [Authorize(Policy = AccessPolicies.Administrator)]
    public async Task<IActionResult> ListTokens(CancellationToken cancellationToken)
    {
        var records = await repository.GetAllOrderedAsync(cancellationToken);
        return Ok(records.Select(r => r.ToExternal()).ToList());
    }

    [HttpPost]
    [Authorize(Policy = AccessPolicies.RecentAdministrator)]
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

        if (!User.TryGetUserId(out var currentUserId)) return Unauthorized();
        var userId = request.UserId ?? currentUserId;
        var targetUser = await identityRepository.FindUserByIdAsync(userId, cancellationToken);
        if (targetUser is null || targetUser.IsDisabled) return BadRequest();

        if (!DevicePathScope.TryNormalizeAbsolutePath(
                request.VirtualRoot, out var virtualRoot))
            return BadRequest(new { error = "VirtualRoot must be an absolute path without traversal segments." });
        if (!await IsDirectoryAsync(virtualRoot, cancellationToken))
            return BadRequest(new { error = "VirtualRoot must identify an existing directory." });

        var now = DateTimeOffset.UtcNow;
        var expiresAt = request.ExpiresAt ?? now + DefaultLifetime;
        if (expiresAt <= now || expiresAt > now + MaximumLifetime)
            return BadRequest(new { error = "ExpiresAt must be in the future and no more than five years away." });

        var plaintext = GenerateToken();
        var hash = BCrypt.Net.BCrypt.HashPassword(plaintext);
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description?.Length > 256) return BadRequest();
        var record = new WebDavToken(
            Guid.NewGuid(),
            userId,
            username,
            hash,
            description,
            now,
            "read",
            virtualRoot,
            expiresAt,
            null);

        await repository.AddAsync(record, cancellationToken);

        return Ok(new External.CreateWebDavTokenResponse(
            record.Id,
            record.Username,
            plaintext,
            record.Description,
            record.CreatedAt,
            record.UserId,
            record.Scope,
            record.VirtualRoot,
            expiresAt));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AccessPolicies.RecentAdministrator)]
    public async Task<IActionResult> DeleteToken([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var removed = await repository.RevokeByIdAsync(
            id, DateTimeOffset.UtcNow, cancellationToken);
        return removed ? NoContent() : NotFound();
    }

    private async Task<bool> IsDirectoryAsync(
        string virtualRoot,
        CancellationToken cancellationToken)
    {
        if (virtualRoot == "/") return true;
        if (await fileMappingRepository.FindByVirtualPathAsync(
                virtualRoot, cancellationToken) is not null)
            return false;
        var children = await fileMappingRepository.GetByVirtualPathPrefixAsync(
            virtualRoot + "/", cancellationToken);
        return children.Count > 0;
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
