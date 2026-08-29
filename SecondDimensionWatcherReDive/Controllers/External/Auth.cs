using System.ComponentModel.DataAnnotations;

namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record LoginData(
    [Required] string Password,
    string? Username = null,
    string? DeviceName = null,
    string? ProfileName = null);

internal sealed record LoginResult(
    string? Token,
    string? RefreshToken,
    bool Success = true,
    Guid? SessionId = null,
    Guid? ProfileId = null);

internal sealed record AuthRequest([Required] string Token, [Required] string RefreshToken);

internal sealed record ReauthenticateRequest(
    [Required] string Password,
    [Required] string RefreshToken);

internal sealed record PasswordConfig(PasswordHash Password);

internal sealed record PasswordHash(string Value);

internal sealed record AuthProfileResponse(
    Guid Id,
    string Name,
    string? Avatar,
    bool HasPin,
    bool IsDefault);

internal sealed record AuthStateResponse(
    Guid UserId,
    string Username,
    string Role,
    Guid SessionId,
    Guid ProfileId,
    IReadOnlyList<AuthProfileResponse> Profiles);
