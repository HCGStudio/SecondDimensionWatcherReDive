namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record MetadataRecognitionRule(
    Guid Id, string Name, bool Enabled, long Revision,
    Guid? SourceFeedId, string? TitlePattern, string? SubtitleGroup,
    string TmdbId, int? FixedSeason, int EpisodeOffset, string? CanonicalGroupName,
    Guid? CreatedFromItemId, DateTimeOffset CreatedAt, DateTimeOffset EffectiveFrom);

public sealed record MetadataRecognitionHit(
    Guid Id, Guid RuleId, string RuleName, long RuleRevision,
    Guid AnimationInfoId, string Title, long ItemRevision, DateTimeOffset AppliedAt);

public sealed class MetadataRecognitionRuleLimitException()
    : InvalidOperationException($"Up to {LogicalDataTransferLimits.MaximumRecognitionRules} recognition rules are supported.");

public interface IMetadataRecognitionRuleRepository
{
    Task<IReadOnlyList<MetadataRecognitionRule>> ListAsync(CancellationToken cancellationToken);
    Task<MetadataRecognitionRule?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> SaveAsync(MetadataRecognitionRule rule, long? expectedRevision, CancellationToken cancellationToken);
    Task<IReadOnlyList<AnimationInfo>> GetCandidatesAsync(Guid? sourceFeedId, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<MetadataRecognitionHit>> GetHitsAsync(Guid? animationInfoId, CancellationToken cancellationToken);
}
