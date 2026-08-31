namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public enum TodoItemType
{
    ReleaseMatched,
    DownloadPendingConfirmation,
    DownloadFailed,
    Incident,
    MetadataReview,
    DiskSpaceLow
}

public enum TodoPriority
{
    Normal,
    High,
    Critical
}

public sealed record TodoItem(
    string Key,
    TodoItemType Type,
    TodoPriority Priority,
    string Title,
    string Detail,
    string DeepLink,
    Guid? ResourceId,
    DateTimeOffset OccurredAt,
    DateTimeOffset? ReadAt,
    DateTimeOffset? SnoozedUntil);

public sealed record TodoPage(
    IReadOnlyList<TodoItem> Items,
    int TotalCount,
    int UnreadCount);

public interface ITodoRepository
{
    Task<TodoPage> GetAsync(
        bool includeRead,
        bool includeSnoozed,
        DateTimeOffset now,
        int skip,
        int take,
        string? focusKey,
        CancellationToken cancellationToken);

    Task SetStateAsync(
        IReadOnlyCollection<string> keys,
        DateTimeOffset? readAt,
        bool updateReadAt,
        DateTimeOffset? snoozedUntil,
        bool updateSnoozedUntil,
        CancellationToken cancellationToken);
}
