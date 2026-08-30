using System.ComponentModel.DataAnnotations;

namespace SecondDimensionWatcherReDive.Configuration;

internal sealed class TokenSecurityOptions
{
    internal const string SectionName = "Authentication";

    [Required]
    public string Issuer { get; set; } = "SecondDimensionWatcherReDive";

    [Required]
    public string Audience { get; set; } = "SecondDimensionWatcherReDive.Client";

    [Range(1, 120)]
    public int AccessTokenMinutes { get; set; } = 10;

    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 30;

    [Range(0, 30)]
    public int RefreshTokenReuseGraceSeconds { get; set; } = 3;

    [Range(1, 120)]
    public int PlaybackLinkMinutes { get; set; } = 15;
}
