namespace SecondDimensionWatcherReDive.Chat.Tools;

// Shared
internal sealed record ToolError(string Error);
internal sealed record ToolSuccess(bool Success, string? Message = null);

// query_animations
internal sealed record AnimationPagedResult(int TotalCount, IEnumerable<AnimationSummary> Items);

internal sealed record AnimationSummary(
    Guid Id, string Title, int? Season, int? Episode,
    bool IsDownloadTracked, bool IsDownloadFinished, bool IsAiProcessed,
    string? AnimationName, string? GroupName, DateTimeOffset PublishTime);

internal sealed record AnimationGroupedToolResult(
    IEnumerable<AnimationGroupItem> Animations,
    int UncategorizedCount,
    IEnumerable<AnimationSummary> Uncategorized);

internal sealed record AnimationGroupItem(
    string TmdbId, string Name, string OriginalName, string? PosterPath,
    int EpisodeCount, IEnumerable<AnimationSummary> Episodes);

internal sealed record AnimationSearchResult(bool Found, AnimationSummary? Item = null);

// manage_feeds
internal sealed record FeedListResult(int Count, IEnumerable<FeedSummary> Feeds);
internal sealed record FeedSummary(Guid Id, string Url, string? Name, DateTimeOffset CreatedAt);
internal sealed record FeedAddResult(bool Success, Guid Id, string Url, string? Name);

// query_season
internal sealed record SeasonListResult(int Count, IEnumerable<BangumiSummary> Bangumis);
internal sealed record BangumiSummary(Guid Id, int MikanId, string Title, int DayOfWeek, string? ImageUrl);
internal sealed record SubgroupListResult(string BangumiTitle, IEnumerable<SubgroupSummary> Subgroups);
internal sealed record SubgroupSummary(int MikanSubgroupId, string Name);

// subscribe_bangumi
internal sealed record SubscribeResult(bool Success, Guid FeedId, string FeedName, string RssUrl);

// manage_tasks
internal sealed record TaskListResult(IEnumerable<TaskSummary> Tasks);
internal sealed record TaskSummary(string Id, string Interval, bool IsEnabled, DateTimeOffset? LastRunAt, bool IsRunning);
internal sealed record TaskRunResult(bool Success, string Message);

// query_files
internal sealed record FileListResult(string AnimationTitle, string Path, List<FileSummary> Files);
internal sealed record FileSummary(string FileName, bool IsDirectory, string Path);
