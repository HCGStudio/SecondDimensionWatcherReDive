using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class TodoRepository(Models.ApplicationContext context) : ITodoRepository
{
    public async Task<TodoPage> GetAsync(
        bool includeRead,
        bool includeSnoozed,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var automation = await context.AnimationInfo
            .AsNoTracking()
            .Where(info => info.AutomationDisposition == SubscriptionAutomationDisposition.Notified
                           || info.AutomationDisposition == SubscriptionAutomationDisposition.PendingConfirmation
                           || info.AutomationDisposition == SubscriptionAutomationDisposition.AutoDownloadFailed)
            .Select(info => new
            {
                info.Id,
                info.Title,
                info.PublishTime,
                info.AutomationDisposition
            })
            .ToListAsync(cancellationToken);

        var incidents = await context.Incidents
            .AsNoTracking()
            .Where(incident => incident.ResolvedAt == null)
            .Select(incident => new
            {
                incident.Id,
                incident.Type,
                incident.Severity,
                incident.Title,
                incident.Detail,
                incident.DetectedAt
            })
            .ToListAsync(cancellationToken);

        var metadata = await context.AnimationInfo
            .AsNoTracking()
            .Where(info => info.MetadataStatus == MetadataReviewStatus.LowConfidence
                           || info.MetadataStatus == MetadataReviewStatus.Failed)
            .Select(info => new
            {
                info.Id,
                info.Title,
                info.MetadataStatus,
                info.MetadataLastError,
                info.PublishTime
            })
            .ToListAsync(cancellationToken);

        var keys = automation.Select(info => $"automation:{info.Id}")
            .Concat(incidents.Select(incident => $"incident:{incident.Id}"))
            .Concat(metadata.Select(info => $"metadata:{info.Id}"))
            .ToArray();
        var states = await context.TodoItemStates
            .AsNoTracking()
            .Where(state => keys.Contains(state.Key))
            .ToDictionaryAsync(state => state.Key, cancellationToken);

        var items = new List<TodoItem>(keys.Length);
        foreach (var info in automation)
        {
            var key = $"automation:{info.Id}";
            var (type, priority, detail) = info.AutomationDisposition switch
            {
                SubscriptionAutomationDisposition.PendingConfirmation =>
                    (TodoItemType.DownloadPendingConfirmation, TodoPriority.High,
                        "A matched release is waiting for download confirmation."),
                SubscriptionAutomationDisposition.AutoDownloadFailed =>
                    (TodoItemType.DownloadFailed, TodoPriority.Critical,
                        "Automatic download could not be started. Review and retry it."),
                _ => (TodoItemType.ReleaseMatched, TodoPriority.Normal,
                    "A notify-only subscription matched this release.")
            };
            items.Add(Create(
                key, type, priority, info.Title, detail,
                $"/todo?focus={Uri.EscapeDataString(key)}", info.Id, info.PublishTime, states));
        }

        foreach (var incident in incidents)
        {
            var key = $"incident:{incident.Id}";
            var disk = incident.Type == IncidentType.DiskSpaceLow;
            items.Add(Create(
                key,
                disk ? TodoItemType.DiskSpaceLow : TodoItemType.Incident,
                incident.Severity == IncidentSeverity.Critical
                    ? TodoPriority.Critical
                    : TodoPriority.High,
                incident.Title,
                incident.Detail,
                disk ? "/incidents?type=diskSpaceLow" : $"/incidents?focus={incident.Id}",
                incident.Id,
                incident.DetectedAt,
                states));
        }

        foreach (var info in metadata)
        {
            var key = $"metadata:{info.Id}";
            items.Add(Create(
                key,
                TodoItemType.MetadataReview,
                info.MetadataStatus == MetadataReviewStatus.Failed
                    ? TodoPriority.High
                    : TodoPriority.Normal,
                info.Title,
                info.MetadataLastError ?? "Metadata confidence is low and needs review.",
                $"/metadata-review?status={(info.MetadataStatus == MetadataReviewStatus.Failed ? "failed" : "lowConfidence")}&focus={info.Id}",
                info.Id,
                info.PublishTime,
                states));
        }

        var unreadCount = items.Count(item => item.ReadAt is null
                                              && (item.SnoozedUntil is null || item.SnoozedUntil <= now));
        var filtered = items
            .Where(item => includeRead || item.ReadAt is null)
            .Where(item => includeSnoozed || item.SnoozedUntil is null || item.SnoozedUntil <= now)
            .OrderByDescending(item => item.Priority)
            .ThenByDescending(item => item.OccurredAt)
            .ToList();
        return new TodoPage(filtered, filtered.Count, unreadCount);
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

    private static TodoItem Create(
        string key,
        TodoItemType type,
        TodoPriority priority,
        string title,
        string detail,
        string deepLink,
        Guid resourceId,
        DateTimeOffset occurredAt,
        IReadOnlyDictionary<string, Models.TodoItemState> states)
    {
        states.TryGetValue(key, out var state);
        return new TodoItem(
            key, type, priority, title, detail, deepLink, resourceId,
            occurredAt, state?.ReadAt, state?.SnoozedUntil);
    }
}
