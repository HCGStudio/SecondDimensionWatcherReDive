namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record AnimationCatalogCursor(
    DateTimeOffset LatestPublishTime,
    string TmdbId,
    long Revision = 0);

public sealed record AnimationInfoCursor(
    DateTimeOffset PublishTime,
    Guid Id,
    long Revision = 0);

public sealed record AnimationCatalogItem(
    string TmdbId,
    string Name,
    string OriginalName,
    string? PosterPath,
    int EpisodeCount,
    int ReleaseCount,
    int AutomationAttentionCount,
    DateTimeOffset LatestPublishTime);

/// <summary>
/// Read projection used by catalog and episode-list APIs. It deliberately
/// excludes download URLs, torrent payloads and storage details.
/// </summary>
public sealed record AnimationInfoSummary(
    Guid Id,
    string Title,
    string Description,
    DateTimeOffset PublishTime,
    bool IsDownloadTracked,
    bool IsDownloadFinished,
    int? Season,
    int? Episode,
    string? GroupName,
    string? AnimationName,
    string? AnimationOriginalName,
    string? AnimationTmdbId,
    string? AnimationPosterPath,
    bool IsAiProcessed,
    Guid? SourceFeedId,
    long? ReleaseSizeBytes,
    SubscriptionAutomationDisposition? AutomationDisposition,
    string? AutomationExplanationJson,
    bool IsMediaLibraryImport);

public sealed record AnimationCatalogPage(
    IReadOnlyList<AnimationCatalogItem> Items,
    AnimationCatalogCursor? NextCursor,
    long Revision = 0,
    bool CursorInvalidated = false);

public sealed record AnimationInfoSummaryPage(
    IReadOnlyList<AnimationInfoSummary> Items,
    AnimationInfoCursor? NextCursor,
    long Revision = 0,
    bool CursorInvalidated = false);

public sealed record AnimationEpisodePage(
    AnimationCatalogItem Animation,
    IReadOnlyList<AnimationInfoSummary> Episodes,
    AnimationInfoCursor? NextCursor,
    long Revision = 0,
    bool CursorInvalidated = false);
