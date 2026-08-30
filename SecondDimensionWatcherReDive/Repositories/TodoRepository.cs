using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class TodoRepository(Models.ApplicationContext context) : ITodoRepository
{
    public async Task<TodoPage> GetAsync(
        bool includeRead,
        bool includeSnoozed,
        DateTimeOffset now,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var automation =
            from info in context.AnimationInfo.AsNoTracking()
            where info.AutomationDisposition == SubscriptionAutomationDisposition.Notified
                  || info.AutomationDisposition == SubscriptionAutomationDisposition.PendingConfirmation
                  || info.AutomationDisposition == SubscriptionAutomationDisposition.AutoDownloadFailed
            let key = "automation:" + info.Id.ToString()
            join candidateState in context.TodoItemStates.AsNoTracking()
                on key equals candidateState.Key into candidateStates
            from state in candidateStates.DefaultIfEmpty()
            select new TodoQueryRow
            {
                Key = key,
                Type = info.AutomationDisposition == SubscriptionAutomationDisposition.PendingConfirmation
                    ? TodoItemType.DownloadPendingConfirmation
                    : info.AutomationDisposition == SubscriptionAutomationDisposition.AutoDownloadFailed
                        ? TodoItemType.DownloadFailed
                        : TodoItemType.ReleaseMatched,
                Priority = info.AutomationDisposition == SubscriptionAutomationDisposition.AutoDownloadFailed
                    ? TodoPriority.Critical
                    : info.AutomationDisposition == SubscriptionAutomationDisposition.PendingConfirmation
                        ? TodoPriority.High
                        : TodoPriority.Normal,
                Title = info.Title,
                Detail = info.AutomationDisposition == SubscriptionAutomationDisposition.PendingConfirmation
                    ? "A matched release is waiting for download confirmation."
                    : info.AutomationDisposition == SubscriptionAutomationDisposition.AutoDownloadFailed
                        ? "Automatic download could not be started. Review and retry it."
                        : "A notify-only subscription matched this release.",
                DeepLink = "/todo?focus=" + key,
                ResourceId = info.Id,
                OccurredAt = info.PublishTime,
                ReadAt = state == null ? null : state.ReadAt,
                SnoozedUntil = state == null ? null : state.SnoozedUntil
            };

        var incidents =
            from incident in context.Incidents.AsNoTracking()
            where incident.ResolvedAt == null
            let baseKey = "incident:" + incident.Id.ToString()
            let key = incident.Occurrence <= 1
                ? baseKey
                : baseKey + ":" + incident.Occurrence.ToString()
            join candidateState in context.TodoItemStates.AsNoTracking()
                on key equals candidateState.Key into candidateStates
            from state in candidateStates.DefaultIfEmpty()
            select new TodoQueryRow
            {
                Key = key,
                Type = incident.Type == IncidentType.DiskSpaceLow
                    ? TodoItemType.DiskSpaceLow
                    : TodoItemType.Incident,
                Priority = incident.Severity == IncidentSeverity.Critical
                    ? TodoPriority.Critical
                    : TodoPriority.High,
                Title = incident.Title,
                Detail = incident.Detail,
                DeepLink = incident.Type == IncidentType.DiskSpaceLow
                    ? "/incidents?type=diskSpaceLow"
                    : "/incidents?focus=" + incident.Id.ToString(),
                ResourceId = incident.Id,
                OccurredAt = incident.UpdatedAt,
                ReadAt = state == null ? null : state.ReadAt,
                SnoozedUntil = state == null ? null : state.SnoozedUntil
            };

        var metadata =
            from info in context.AnimationInfo.AsNoTracking()
            where info.MetadataStatus == MetadataReviewStatus.LowConfidence
                  || info.MetadataStatus == MetadataReviewStatus.Failed
            let key = "metadata:" + info.Id.ToString()
            join candidateState in context.TodoItemStates.AsNoTracking()
                on key equals candidateState.Key into candidateStates
            from state in candidateStates.DefaultIfEmpty()
            select new TodoQueryRow
            {
                Key = key,
                Type = TodoItemType.MetadataReview,
                Priority = info.MetadataStatus == MetadataReviewStatus.Failed
                    ? TodoPriority.High
                    : TodoPriority.Normal,
                Title = info.Title,
                Detail = info.MetadataLastError ?? "Metadata confidence is low and needs review.",
                DeepLink = info.MetadataStatus == MetadataReviewStatus.Failed
                    ? "/metadata-review?status=failed&focus=" + info.Id.ToString()
                    : "/metadata-review?status=lowConfidence&focus=" + info.Id.ToString(),
                ResourceId = info.Id,
                OccurredAt = info.PublishTime,
                ReadAt = state == null ? null : state.ReadAt,
                SnoozedUntil = state == null ? null : state.SnoozedUntil
            };

        var allItems = automation.Concat(incidents).Concat(metadata);
        var unreadCount = await allItems.CountAsync(
            item => item.ReadAt == null
                    && (item.SnoozedUntil == null || item.SnoozedUntil <= now),
            cancellationToken);

        var visibleItems = allItems;
        if (!includeRead)
            visibleItems = visibleItems.Where(item => item.ReadAt == null);
        if (!includeSnoozed)
            visibleItems = visibleItems.Where(
                item => item.SnoozedUntil == null || item.SnoozedUntil <= now);

        var totalCount = await visibleItems.CountAsync(cancellationToken);
        var rows = await visibleItems
            .OrderByDescending(item => item.Priority)
            .ThenByDescending(item => item.OccurredAt)
            .ThenBy(item => item.Key)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new TodoPage(
            rows.Select(item => new TodoItem(
                item.Key,
                item.Type,
                item.Priority,
                item.Title,
                item.Detail,
                item.DeepLink,
                item.ResourceId,
                item.OccurredAt,
                item.ReadAt,
                item.SnoozedUntil)).ToList(),
            totalCount,
            unreadCount);
    }

    public async Task SetStateAsync(
        IReadOnlyCollection<string> keys,
        DateTimeOffset? readAt,
        bool updateReadAt,
        DateTimeOffset? snoozedUntil,
        bool updateSnoozedUntil,
        CancellationToken cancellationToken)
    {
        var existing = await context.TodoItemStates
            .Where(state => keys.Contains(state.Key))
            .ToDictionaryAsync(state => state.Key, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var key in keys)
        {
            if (!existing.TryGetValue(key, out var state))
            {
                state = new Models.TodoItemState { Key = key };
                await context.TodoItemStates.AddAsync(state, cancellationToken);
            }
            if (updateReadAt) state.ReadAt = readAt;
            if (updateSnoozedUntil) state.SnoozedUntil = snoozedUntil;
            state.UpdatedAt = now;
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    private sealed class TodoQueryRow
    {
        public string Key { get; init; } = string.Empty;
        public TodoItemType Type { get; init; }
        public TodoPriority Priority { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public string DeepLink { get; init; } = string.Empty;
        public Guid? ResourceId { get; init; }
        public DateTimeOffset OccurredAt { get; init; }
        public DateTimeOffset? ReadAt { get; init; }
        public DateTimeOffset? SnoozedUntil { get; init; }
    }
}
