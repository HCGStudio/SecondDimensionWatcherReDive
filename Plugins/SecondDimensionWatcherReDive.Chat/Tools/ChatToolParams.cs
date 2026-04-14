using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Chat.Tools;

internal enum QueryAnimationsAction
{
    List,
    Grouped,
    Downloading,
    Downloaded,
    SearchByTitle,
    GetById
}

internal sealed class QueryAnimationsParams
{
    /// <summary>Query action</summary>
    public required QueryAnimationsAction Action { get; init; }

    /// <summary>Number of items to skip for pagination, default 0</summary>
    public int? Skip { get; init; }

    /// <summary>Number of items to take for pagination, default 10</summary>
    public int? Take { get; init; }

    /// <summary>Search title, required when action=search_by_title</summary>
    public string? Title { get; init; }

    /// <summary>Animation ID, required when action=get_by_id</summary>
    public string? Id { get; init; }
}

internal enum ManageFeedsAction
{
    List,
    Add,
    Remove
}

internal sealed class ManageFeedsParams
{
    /// <summary>Action</summary>
    public required ManageFeedsAction Action { get; init; }

    /// <summary>RSS URL, required when action=add</summary>
    public string? Url { get; init; }

    /// <summary>Feed name, optional when action=add</summary>
    public string? Name { get; init; }

    /// <summary>Feed ID, required when action=remove</summary>
    public string? Id { get; init; }
}

internal enum QuerySeasonAction
{
    CurrentSeason,
    BrowseSeason,
    Subgroups
}

internal sealed class QuerySeasonParams
{
    /// <summary>Query action</summary>
    public required QuerySeasonAction Action { get; init; }

    /// <summary>Year, optional when action=browse_season</summary>
    public int? Year { get; init; }

    /// <summary>Season (spring/summer/autumn/winter), optional when action=browse_season</summary>
    public AnimeSeason? Season { get; init; }

    /// <summary>Mikan bangumi ID, required when action=subgroups</summary>
    public int? MikanId { get; init; }
}

internal sealed class SubscribeBangumiParams
{
    /// <summary>Mikan bangumi ID</summary>
    public required int MikanId { get; init; }

    /// <summary>Subgroup ID, subscribes to all subgroups if not specified</summary>
    public int? SubgroupId { get; init; }
}

internal enum ManageTasksAction
{
    List,
    Run
}

internal sealed class ManageTasksParams
{
    /// <summary>Action</summary>
    public required ManageTasksAction Action { get; init; }

    /// <summary>Task ID, required when action=run</summary>
    public string? TaskId { get; init; }
}

internal enum ManageDownloadsAction
{
    Start,
    Pause,
    Resume,
    Cancel
}

internal sealed class ManageDownloadsParams
{
    /// <summary>Action</summary>
    public required ManageDownloadsAction Action { get; init; }

    /// <summary>AnimationInfo ID</summary>
    public required string AnimationId { get; init; }

    /// <summary>Whether to also delete files when cancelling, default false</summary>
    public bool? RemoveFile { get; init; }
}

internal sealed class QueryFilesParams
{
    /// <summary>AnimationInfo ID</summary>
    public required string AnimationId { get; init; }

    /// <summary>Relative subdirectory path, lists root directory if not specified</summary>
    public string? RelativeDir { get; init; }
}
