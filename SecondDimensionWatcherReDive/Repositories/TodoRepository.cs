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
        string? focusKey,
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
                    ? "/incidents?type=diskSpaceLow&focus=" + incident.Id.ToString()
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

        if (focusKey is not null && rows.All(item => item.Key != focusKey))
        {
            // Notification deep links must remain actionable even when the
            // target is outside the current page or was already read/snoozed.
            var focused = await allItems
                .Where(item => item.Key == focusKey)
                .SingleOrDefaultAsync(cancellationToken);
            if (focused is not null)
                rows.Insert(0, focused);
        }

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
        var validKeys = await ResolveCurrentKeysAsync(keys, cancellationToken);
        if (validKeys.Length == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        // One set-based upsert avoids the insert race produced when two tabs
        // update a previously untouched todo at the same time.
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "TodoItemStates" ("Key", "ReadAt", "SnoozedUntil", "UpdatedAt")
            SELECT input."Key", {{readAt}}, {{snoozedUntil}}, {{now}}
            FROM unnest({{validKeys}}) AS input("Key")
            ON CONFLICT ("Key") DO UPDATE SET
                "ReadAt" = CASE
                    WHEN {{updateReadAt}} THEN EXCLUDED."ReadAt"
                    ELSE "TodoItemStates"."ReadAt"
                END,
                "SnoozedUntil" = CASE
                    WHEN {{updateSnoozedUntil}} THEN EXCLUDED."SnoozedUntil"
                    ELSE "TodoItemStates"."SnoozedUntil"
                END,
                "UpdatedAt" = EXCLUDED."UpdatedAt"
            """, cancellationToken);

        // Mark-unread/unsnooze of an otherwise pristine item does not need a
        // tombstone. Keeping the table limited to meaningful state also bounds
        // joins on long-lived installations.
        await context.TodoItemStates
            .Where(state => validKeys.Contains(state.Key)
                            && state.ReadAt == null
                            && state.SnoozedUntil == null)
            .ExecuteDeleteAsync(cancellationToken);

        var stillCurrent = await ResolveCurrentKeysAsync(validKeys, cancellationToken);
        var staleKeys = validKeys.Except(stillCurrent, StringComparer.Ordinal).ToArray();
        if (staleKeys.Length > 0)
        {
            await context.TodoItemStates
                .Where(state => staleKeys.Contains(state.Key))
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    private async Task<string[]> ResolveCurrentKeysAsync(
        IReadOnlyCollection<string> requestedKeys,
        CancellationToken cancellationToken)
    {
        var requested = requestedKeys.ToHashSet(StringComparer.Ordinal);
        var automationIds = new HashSet<Guid>();
        var metadataIds = new HashSet<Guid>();
        var incidentIds = new HashSet<Guid>();
        foreach (var key in requested)
        {
            var parts = key.Split(':');
            if (parts.Length < 2 || !Guid.TryParse(parts[1], out var id))
                continue;
            switch (parts[0])
            {
                case "automation":
                    automationIds.Add(id);
                    break;
                case "metadata":
                    metadataIds.Add(id);
                    break;
                case "incident":
                    incidentIds.Add(id);
                    break;
            }
        }

        var valid = new HashSet<string>(StringComparer.Ordinal);
        if (automationIds.Count > 0)
        {
            var ids = await context.AnimationInfo
                .AsNoTracking()
                .Where(info => automationIds.Contains(info.Id)
                               && (info.AutomationDisposition == SubscriptionAutomationDisposition.Notified
                                   || info.AutomationDisposition == SubscriptionAutomationDisposition.PendingConfirmation
                                   || info.AutomationDisposition == SubscriptionAutomationDisposition.AutoDownloadFailed))
                .Select(info => info.Id)
                .ToListAsync(cancellationToken);
            foreach (var id in ids)
                valid.Add("automation:" + id);
        }

        if (metadataIds.Count > 0)
        {
            var ids = await context.AnimationInfo
                .AsNoTracking()
                .Where(info => metadataIds.Contains(info.Id)
                               && (info.MetadataStatus == MetadataReviewStatus.LowConfidence
                                   || info.MetadataStatus == MetadataReviewStatus.Failed))
                .Select(info => info.Id)
                .ToListAsync(cancellationToken);
            foreach (var id in ids)
                valid.Add("metadata:" + id);
        }

        if (incidentIds.Count > 0)
        {
            var incidents = await context.Incidents
                .AsNoTracking()
                .Where(incident => incidentIds.Contains(incident.Id)
                                   && incident.ResolvedAt == null)
                .Select(incident => new { incident.Id, incident.Occurrence })
                .ToListAsync(cancellationToken);
            foreach (var incident in incidents)
            {
                valid.Add(incident.Occurrence <= 1
                    ? "incident:" + incident.Id
                    : $"incident:{incident.Id}:{incident.Occurrence}");
            }
        }

        return valid
            .Where(requested.Contains)
            .Order(StringComparer.Ordinal)
            .ToArray();
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
