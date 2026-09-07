namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record MultiSourceSubscription(Guid Id, string Name, string TmdbId, int Season,
    IReadOnlyList<Guid> FeedIds, int WaitMinutes, string Mode, IReadOnlyList<string> SubtitleGroups,
    IReadOnlyList<string> Resolutions, IReadOnlyList<string> Codecs, IReadOnlyList<string> Languages,
    long? MinSizeBytes, long? MaxSizeBytes, IReadOnlyList<string> ExcludedKeywords,
    bool EnableVersionUpgrade, int MinimumUpgradeScore, int UpgradeRollbackHours,
    DateTimeOffset CreatedAt = default, DateTimeOffset UpdatedAt = default)
{
    public SubscriptionAutomationPolicy ToPolicy(Guid feedId) => new(feedId, SubtitleGroups, Resolutions, Codecs,
        Languages, MinSizeBytes, MaxSizeBytes, ExcludedKeywords, Enum.Parse<SubscriptionAutomationMode>(Mode),
        CreatedAt, UpdatedAt, EnableVersionUpgrade, MinimumUpgradeScore, UpgradeRollbackHours);
}
public sealed record MultiSourceEpisodeDecision(Guid SubscriptionId, int Episode, DateTimeOffset WaitStartedAt,
    DateTimeOffset WaitUntil, Guid? SelectedReleaseId, string Outcome, string Reason, DateTimeOffset UpdatedAt,
    string? SelectedTitle = null, Guid? SelectedSourceFeedId = null);
public sealed record MultiSourceFeedStatus(Guid FeedId, string Name, int Priority, DateTimeOffset? LatestPublishedAt,
    string? LatestTitle, int UnidentifiedCount);
public sealed record MultiSourceSubscriptionStatus(MultiSourceSubscription Subscription,
    IReadOnlyList<MultiSourceFeedStatus> Sources, IReadOnlyList<MultiSourceEpisodeDecision> Decisions);
public interface IMultiSourceSubscriptionRepository
{
    Task<IReadOnlyList<MultiSourceSubscription>> GetAllAsync(CancellationToken cancellationToken);
    Task<MultiSourceSubscription?> FindByFeedIdAsync(Guid feedId, CancellationToken cancellationToken);
    Task<MultiSourceSubscription> SaveAsync(MultiSourceSubscription subscription, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<MultiSourceFeedStatus>> GetSourceStatusAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<MultiSourceEpisodeDecision>> GetDecisionsAsync(Guid id, CancellationToken cancellationToken);
    Task<MultiSourceEpisodeDecision?> SaveDecisionAsync(MultiSourceEpisodeDecision decision,
        MultiSourceSubscription expectedSubscription, CancellationToken cancellationToken);
}
