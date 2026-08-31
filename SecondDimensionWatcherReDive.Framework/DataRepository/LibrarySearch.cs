namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public enum LibraryDownloadState
{
    Any,
    NotDownloaded,
    Downloading,
    Downloaded
}

public enum LibraryWatchState
{
    Any,
    Unwatched,
    InProgress,
    Watched
}

public enum LibrarySourceKind
{
    Any,
    Torrent,
    MediaLibraryImport
}

public enum LibrarySearchSort
{
    PublishedDescending,
    TitleAscending,
    EpisodeAscending,
    ScoreDescending
}

public sealed record LibrarySearchRequest(
    string? Query,
    int? Season,
    int? Episode,
    string? SubtitleGroup,
    string? Resolution,
    string? Codec,
    string? Language,
    LibraryDownloadState DownloadState,
    LibraryWatchState WatchState,
    string? VirtualPath,
    LibrarySourceKind Source,
    LibrarySearchSort Sort,
    string? Cursor,
    int Take,
    Guid UserId);

public sealed record LibrarySearchItem(
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

public sealed record LibrarySearchResult(
    IReadOnlyList<LibrarySearchItem> Items,
    string? NextCursor);

public sealed record EpisodeDuplicate(
    int Episode,
    IReadOnlyList<Guid> ReleaseIds);

public sealed record ReleaseUpgradeCandidate(
    Guid CurrentReleaseId,
    Guid CandidateReleaseId,
    string AnimationName,
    int Season,
    int Episode,
    int CurrentScore,
    int CandidateScore,
    IReadOnlyList<string> ScoreReasons,
    bool Automatic);

public sealed record LibraryIntegritySummary(
    string TmdbId,
    string AnimationName,
    int Season,
    int? ExpectedEpisodeCount,
    IReadOnlyList<int> MissingEpisodes,
    IReadOnlyList<EpisodeDuplicate> DuplicateEpisodes,
    int UnidentifiedReleaseCount,
    IReadOnlyList<ReleaseUpgradeCandidate> UpgradeCandidates);
