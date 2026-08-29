using System.ComponentModel.DataAnnotations;

namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record CreateProfileRequest(
    [Required] string Name,
    string? Avatar,
    string? Pin);

internal sealed record UpdateProfileRequest(
    [Required] string Name,
    string? Avatar,
    string? Pin,
    string? CurrentPin = null,
    bool ReplacePin = false);

internal sealed record SwitchProfileRequest(
    Guid ProfileId,
    string? Pin,
    [Required] string RefreshToken);

internal sealed record SessionResponse(
    Guid Id,
    Guid UserId,
    string Username,
    Guid ProfileId,
    string ProfileName,
    string? DeviceName,
    DateTimeOffset AuthenticatedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    bool IsCurrent);

internal sealed record UserResponse(
    Guid Id,
    string Username,
    string Role,
    bool IsDisabled,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AuthProfileResponse> Profiles);

internal sealed record CreateUserRequest(
    [Required] string Username,
    [Required] string Password,
    [Required] string Role,
    [Required] string ProfileName);

internal sealed record UpdateUserAccessRequest(
    [Required] string Role,
    bool IsDisabled);
