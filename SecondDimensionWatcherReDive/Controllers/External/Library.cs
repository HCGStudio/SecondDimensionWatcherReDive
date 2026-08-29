namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record ExecuteReleaseUpgradeRequest(
    Guid CurrentReleaseId,
    Guid CandidateReleaseId,
    bool DryRun = true);

internal sealed record LibrarySearchItemResponse(
    Guid AnimationInfoId,
    string Title,
    string? AnimationName,
    string? AnimationOriginalName,
    string? TmdbId,
    int? Season,
    int? Episode,
    string? SubtitleGroup,
    string? Resolution,
    string? Codec,
    IReadOnlyList<string> Languages,
    bool IsDownloadTracked,
    bool IsDownloadFinished,
    bool IsMediaLibraryImport,
    bool IsWatched,
    double? PlaybackPositionSeconds,
    IReadOnlyList<string> VirtualPaths,
    int ReleaseScore,
    IReadOnlyList<string> ScoreReasons,
    DateTimeOffset PublishedAt);

internal sealed record LibrarySearchResponse(
    IReadOnlyList<LibrarySearchItemResponse> Items,
    string? NextCursor);

internal sealed record EpisodeDuplicateResponse(
    int Episode,
    IReadOnlyList<Guid> ReleaseIds);

internal sealed record ReleaseUpgradeCandidateResponse(
    Guid CurrentReleaseId,
    Guid CandidateReleaseId,
    string AnimationName,
    int Season,
    int Episode,
    int CurrentScore,
    int CandidateScore,
    IReadOnlyList<string> ScoreReasons,
    bool Automatic);

internal sealed record LibraryIntegritySummaryResponse(
    string TmdbId,
    string AnimationName,
    int Season,
    int? ExpectedEpisodeCount,
    IReadOnlyList<int> MissingEpisodes,
    IReadOnlyList<EpisodeDuplicateResponse> DuplicateEpisodes,
    int UnidentifiedReleaseCount,
    IReadOnlyList<ReleaseUpgradeCandidateResponse> UpgradeCandidates);

internal sealed record ReleaseUpgradeOperationResponse(
    Guid Id,
    Guid CurrentReleaseId,
    Guid CandidateReleaseId,
    string Status,
    int CurrentScore,
    int CandidateScore,
    DateTimeOffset CreatedAt,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? AppliedAt,
    DateTimeOffset? RollbackUntil,
    DateTimeOffset? CompletedAt,
    string? FailureSummary);

internal sealed record ReleaseUpgradeMutationResponse(
    bool IsSuccess,
    string Outcome,
    ReleaseUpgradeOperationResponse? Operation);

internal sealed record ReleaseUpgradeExecutionResponse(
    bool IsSuccess,
    string Outcome,
    bool DryRun,
    bool RequiresDownload,
    ReleaseUpgradeOperationResponse? Operation,
    IReadOnlyList<string> ValidationErrors);
