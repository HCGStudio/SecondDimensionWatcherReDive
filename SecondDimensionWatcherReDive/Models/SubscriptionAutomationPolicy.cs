using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Models;

public class SubscriptionAutomationPolicy
{
    public Guid FeedId { get; set; }

    public Feed Feed { get; set; } = null!;

    public string[] SubtitleGroups { get; set; } = [];

    public string[] Resolutions { get; set; } = [];

    public string[] Codecs { get; set; } = [];

    public string[] Languages { get; set; } = [];

    public long? MinSizeBytes { get; set; }

    public long? MaxSizeBytes { get; set; }

    public string[] ExcludedKeywords { get; set; } = [];

    public SubscriptionAutomationMode Mode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool EnableVersionUpgrade { get; set; }

    public int MinimumUpgradeScore { get; set; } = 25;

    public int UpgradeRollbackHours { get; set; } = 72;
}
