using System.ComponentModel.DataAnnotations;

namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record LoginData([Required] string Password);

internal sealed record LoginResult(string? Token, string? RefreshToken, bool Success = true);

internal sealed record AuthRequest([Required] string Token, [Required] string RefreshToken);

internal sealed record RevokeTokenRequest(
    [Required]
    [StringLength(64, MinimumLength = 32)]
    [RegularExpression("^[A-Za-z0-9_-]+$")]
    string RefreshToken);

internal sealed record PasswordConfig(PasswordHash Password);

internal sealed record PasswordHash(string Value);
