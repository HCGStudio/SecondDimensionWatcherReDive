using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class LogicalDataTransferRepository(Models.ApplicationContext context)
    : ILogicalDataTransferRepository
{
    private const int FormatVersion = 1;

    public async Task<LogicalDataBundle> ExportAsync(
        LogicalDataCategory categories,
        Guid userId,
        string applicationVersion,
        CancellationToken cancellationToken)
    {
        var feeds = categories.HasFlag(LogicalDataCategory.Feeds)
            ? await context.Feeds.AsNoTracking()
                .OrderBy(feed => feed.CreatedAt)
                .Select(feed => new LogicalFeed(feed.Id, feed.Url, feed.Name, feed.CreatedAt))
                .ToListAsync(cancellationToken)
            : [];

        var policies = categories.HasFlag(LogicalDataCategory.AutomationPolicies)
            ? await context.SubscriptionAutomationPolicies.AsNoTracking()
                .OrderBy(policy => policy.Feed.Url)
                .Select(policy => new LogicalAutomationPolicy(
                    policy.Feed.Url,
                    policy.SubtitleGroups,
                    policy.Resolutions,
                    policy.Codecs,
                    policy.Languages,
                    policy.MinSizeBytes,
                    policy.MaxSizeBytes,
                    policy.ExcludedKeywords,
                    policy.Mode,
                    policy.CreatedAt,
                    policy.UpdatedAt))
                .ToListAsync(cancellationToken)
            : [];

        var rules = categories.HasFlag(LogicalDataCategory.FileNameRules)
            ? await (from rule in context.FileNameRegexRules.AsNoTracking()
                    join animation in context.Animations.AsNoTracking()
                        on rule.AnimationId equals animation.Id
                    orderby animation.TmdbId, rule.CreatedAt
                    select new LogicalFileNameRule(
                        rule.Id,
                        animation.TmdbId,
                        animation.Name,
                        animation.OriginalName,
                        animation.PosterPath,
                        rule.Pattern,
                        rule.Description,
                        rule.CreatedAt))
                .ToListAsync(cancellationToken)
            : [];

        var corrections = categories.HasFlag(LogicalDataCategory.MetadataCorrections)
            ? await context.MetadataReviewOperations.AsNoTracking()
                .Where(operation => operation.State == MetadataReviewOperationState.Applied
                                    && operation.AppliedAt != null
                                    && operation.AnimationInfo.CurrentMetadataReviewOperationId == operation.Id)
                .OrderBy(operation => operation.AppliedAt)
                .Select(operation => new LogicalMetadataCorrection(
                    operation.Id,
                    operation.AnimationInfo.DownloadUrl,
                    operation.AnimationInfo.Title,
                    operation.AnimationInfo.PublishTime,
                    operation.ProposedAnimationTmdbId,
                    operation.ProposedAnimationName,
                    operation.ProposedAnimationOriginalName,
                    operation.ProposedAnimationPosterPath,
                    operation.ProposedDescription,
                    operation.ProposedSeason,
                    operation.ProposedEpisode,
                    operation.ProposedGroupName,
                    operation.AppliedAt!.Value))
                .ToListAsync(cancellationToken)
            : [];

        var progress = categories.HasFlag(LogicalDataCategory.Playback)
            ? await context.PlaybackProgresses.AsNoTracking()
                .Where(item => item.UserId == userId)
                .OrderBy(item => item.VirtualPath)
                .Select(item => new LogicalPlaybackProgress(
                    item.VirtualPath,
                    item.PositionSeconds,
                    item.DurationSeconds,
                    item.IsWatched,
                    item.UpdatedAt,
                    item.WatchedAt))
                .ToListAsync(cancellationToken)
            : [];

        LogicalPlaybackPreferences? preferences = null;
        if (categories.HasFlag(LogicalDataCategory.Playback))
        {
            preferences = await context.PlaybackPreferences.AsNoTracking()
                .Where(item => item.UserId == userId)
                .Select(item => new LogicalPlaybackPreferences(
                    item.SubtitleLanguage,
                    item.SubtitleTrackLabel,
                    item.AudioLanguage,
                    item.AudioTrackLabel,
                    item.AutoPlayNext,
                    item.UpdatedAt))
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new LogicalDataBundle(
            FormatVersion,
            DateTimeOffset.UtcNow,
            applicationVersion,
            categories,
            feeds,
            policies,
            rules,
            corrections,
            progress,
            preferences);
    }

    public async Task<LogicalImportResult> ImportAsync(
        LogicalDataBundle bundle,
        LogicalImportConflictStrategy conflictStrategy,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (bundle.FormatVersion != FormatVersion)
            throw new ArgumentException($"Unsupported logical data format {bundle.FormatVersion}.", nameof(bundle));

        var statistics = new ImportStatistics();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var feedsByUrl = await context.Feeds
            .ToDictionaryAsync(feed => feed.Url, StringComparer.Ordinal, cancellationToken);
        var usedFeedIds = await context.Feeds.AsNoTracking()
            .Select(feed => feed.Id)
            .ToHashSetAsync(cancellationToken);
        await ImportFeedsAsync(bundle, conflictStrategy, feedsByUrl, usedFeedIds, statistics, cancellationToken);
        await ImportPoliciesAsync(bundle, conflictStrategy, feedsByUrl, statistics, cancellationToken);
        await ImportRulesAsync(bundle, conflictStrategy, statistics, cancellationToken);
        await ImportMetadataCorrectionsAsync(bundle, conflictStrategy, statistics, cancellationToken);
        await ImportPlaybackAsync(bundle, conflictStrategy, userId, statistics, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return statistics.ToResult();
    }

    private async Task ImportFeedsAsync(
        LogicalDataBundle bundle,
        LogicalImportConflictStrategy strategy,
        IDictionary<string, Models.Feed> feedsByUrl,
        ISet<Guid> usedIds,
        ImportStatistics statistics,
        CancellationToken cancellationToken)
    {
        if (!bundle.Categories.HasFlag(LogicalDataCategory.Feeds))
            return;

        foreach (var imported in bundle.Feeds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (feedsByUrl.TryGetValue(imported.Url, out var existing))
            {
                if (existing.Name == imported.Name)
                {
                    statistics.Skip();
                    continue;
                }
                if (!HandleConflict(strategy, Identifier("feed", imported.Url), statistics))
                    continue;
                existing.Name = imported.Name;
                statistics.Update();
                continue;
            }

            var id = usedIds.Add(imported.Id) ? imported.Id : Guid.NewGuid();
            var entity = new Models.Feed
            {
                Id = id,
                Url = imported.Url,
                Name = imported.Name,
                CreatedAt = imported.CreatedAt
            };
            context.Feeds.Add(entity);
            feedsByUrl.Add(entity.Url, entity);
            statistics.Add();
        }
        await Task.CompletedTask;
    }

    private async Task ImportPoliciesAsync(
        LogicalDataBundle bundle,
        LogicalImportConflictStrategy strategy,
        IReadOnlyDictionary<string, Models.Feed> feedsByUrl,
        ImportStatistics statistics,
        CancellationToken cancellationToken)
    {
        if (!bundle.Categories.HasFlag(LogicalDataCategory.AutomationPolicies))
            return;

        var existing = await context.SubscriptionAutomationPolicies
            .ToDictionaryAsync(policy => policy.FeedId, cancellationToken);
        foreach (var imported in bundle.AutomationPolicies)
        {
            if (!feedsByUrl.TryGetValue(imported.FeedUrl, out var feed))
            {
                statistics.Skip($"policy feed is missing:{Identifier("feed", imported.FeedUrl)}");
                continue;
            }

            if (existing.TryGetValue(feed.Id, out var entity))
            {
                if (!HandleConflict(strategy, Identifier("policy", imported.FeedUrl), statistics))
                    continue;
                ApplyPolicy(imported, entity);
                statistics.Update();
                continue;
            }

            entity = new Models.SubscriptionAutomationPolicy { FeedId = feed.Id, Feed = feed };
            ApplyPolicy(imported, entity);
            context.SubscriptionAutomationPolicies.Add(entity);
            existing.Add(feed.Id, entity);
            statistics.Add();
        }
    }

    private async Task ImportRulesAsync(
        LogicalDataBundle bundle,
        LogicalImportConflictStrategy strategy,
        ImportStatistics statistics,
        CancellationToken cancellationToken)
    {
        if (!bundle.Categories.HasFlag(LogicalDataCategory.FileNameRules))
            return;

        var animations = await context.Animations
            .ToDictionaryAsync(animation => animation.TmdbId, StringComparer.Ordinal, cancellationToken);
        var rules = await context.FileNameRegexRules.ToListAsync(cancellationToken);
        var ruleByKey = rules.ToDictionary(rule => (rule.AnimationId, rule.Pattern));
        var usedRuleIds = rules.Select(rule => rule.Id).ToHashSet();

        foreach (var imported in bundle.FileNameRules)
        {
            if (!animations.TryGetValue(imported.AnimationTmdbId, out var animation))
            {
                animation = new Models.Animation
                {
                    Id = Guid.NewGuid(),
                    TmdbId = imported.AnimationTmdbId,
                    Name = imported.AnimationName,
                    OriginalName = imported.AnimationOriginalName,
                    PosterPath = imported.AnimationPosterPath
                };
                context.Animations.Add(animation);
                animations.Add(animation.TmdbId, animation);
            }

            if (ruleByKey.TryGetValue((animation.Id, imported.Pattern), out var existing))
            {
                if (existing.Description == imported.Description)
                {
                    statistics.Skip();
                    continue;
                }
                if (!HandleConflict(strategy,
                        $"filename-rule:{imported.AnimationTmdbId}:{imported.Pattern}", statistics))
                    continue;
                existing.Description = imported.Description;
                statistics.Update();
                continue;
            }

            var id = usedRuleIds.Add(imported.Id) ? imported.Id : Guid.NewGuid();
            var entity = new Models.FileNameRegexRule
            {
                Id = id,
                AnimationId = animation.Id,
                Pattern = imported.Pattern,
                Description = imported.Description,
                CreatedAt = imported.CreatedAt
            };
            context.FileNameRegexRules.Add(entity);
            ruleByKey.Add((animation.Id, entity.Pattern), entity);
            statistics.Add();
        }
    }

    private async Task ImportMetadataCorrectionsAsync(
        LogicalDataBundle bundle,
        LogicalImportConflictStrategy strategy,
        ImportStatistics statistics,
        CancellationToken cancellationToken)
    {
        if (!bundle.Categories.HasFlag(LogicalDataCategory.MetadataCorrections) ||
            bundle.MetadataCorrections.Count == 0)
            return;

        var downloadUrls = bundle.MetadataCorrections.Select(item => item.ReleaseDownloadUrl).Distinct().ToArray();
        var candidates = await context.AnimationInfo
            .Include(info => info.Animation)
            .Include(info => info.Group)
            .Where(info => downloadUrls.Contains(info.DownloadUrl))
            .ToListAsync(cancellationToken);
        var byKey = candidates
            .GroupBy(info => (info.DownloadUrl, info.Title, info.PublishTime.ToUnixTimeSeconds()))
            .ToDictionary(group => group.Key, group => group.ToList());
        var operationIds = bundle.MetadataCorrections.Select(item => item.OperationId).ToArray();
        var existingOperationIds = await context.MetadataReviewOperations.AsNoTracking()
            .Where(operation => operationIds.Contains(operation.Id))
            .Select(operation => operation.Id)
            .ToHashSetAsync(cancellationToken);
        var animations = await context.Animations
            .ToDictionaryAsync(animation => animation.TmdbId, StringComparer.Ordinal, cancellationToken);
        var groups = await context.AnimationGroups
            .ToDictionaryAsync(group => group.Name, StringComparer.Ordinal, cancellationToken);

        foreach (var imported in bundle.MetadataCorrections)
        {
            if (existingOperationIds.Contains(imported.OperationId))
            {
                statistics.Skip();
                continue;
            }
            if (!byKey.TryGetValue(
                    (imported.ReleaseDownloadUrl, imported.ReleaseTitle,
                        imported.ReleasePublishTime.ToUnixTimeSeconds()),
                    out var matches) || matches.Count != 1)
            {
                statistics.Skip($"metadata release is missing or ambiguous:{imported.ReleaseTitle}");
                continue;
            }

            var info = matches[0];
            if (info.CurrentMetadataReviewOperationId is not null &&
                !HandleConflict(strategy, $"metadata:{imported.ReleaseTitle}", statistics))
                continue;

            if (!animations.TryGetValue(imported.AnimationTmdbId, out var animation))
            {
                animation = new Models.Animation
                {
                    Id = Guid.NewGuid(),
                    TmdbId = imported.AnimationTmdbId
                };
                context.Animations.Add(animation);
                animations.Add(animation.TmdbId, animation);
            }
            animation.Name = imported.AnimationName;
            animation.OriginalName = imported.AnimationOriginalName;
            animation.PosterPath = imported.AnimationPosterPath;

            Models.AnimationGroup? group = null;
            if (!string.IsNullOrWhiteSpace(imported.GroupName) &&
                !groups.TryGetValue(imported.GroupName, out group))
            {
                group = new Models.AnimationGroup { Id = Guid.NewGuid(), Name = imported.GroupName };
                context.AnimationGroups.Add(group);
                groups.Add(group.Name, group);
            }

            var nextVersion = checked(info.StateVersion + 1);
            var operation = new Models.MetadataReviewOperation
            {
                Id = imported.OperationId,
                AnimationInfoId = info.Id,
                AnimationInfo = info,
                State = MetadataReviewOperationState.Applied,
                CreatedAt = imported.AppliedAt,
                ExpiresAt = imported.AppliedAt.AddDays(1),
                BaseVersion = info.StateVersion,
                BaseFileStore = info.FileStore,
                BaseStorePath = info.StorePath,
                BaseIsDownloadFinished = info.IsDownloadFinished,
                ProposedAnimationTmdbId = imported.AnimationTmdbId,
                ProposedAnimationName = imported.AnimationName,
                ProposedAnimationOriginalName = imported.AnimationOriginalName,
                ProposedAnimationPosterPath = imported.AnimationPosterPath,
                ProposedDescription = imported.Description,
                ProposedSeason = imported.Season,
                ProposedEpisode = imported.Episode,
                ProposedGroupName = imported.GroupName,
                AppliedAt = imported.AppliedAt,
                AppliedVersion = nextVersion,
                PreviousDescription = info.Description,
                PreviousAnimationId = info.Animation?.Id,
                PreviousGroupId = info.Group?.Id,
                PreviousSeason = info.Season,
                PreviousEpisode = info.Episode,
                PreviousMetadataStatus = info.MetadataStatus,
                PreviousConfidence = info.MetadataConfidence,
                PreviousLastError = info.MetadataLastError,
                PreviousIsAiProcessed = info.IsAiProcessed,
                PreviousAiRetryCount = info.AiRetryCount,
                PreviousReviewedAt = info.MetadataReviewedAt,
                PreviousCurrentOperationId = info.CurrentMetadataReviewOperationId
            };
            context.MetadataReviewOperations.Add(operation);
            info.Animation = animation;
            info.Group = group;
            info.Description = imported.Description;
            info.Season = imported.Season;
            info.Episode = imported.Episode;
            info.MetadataStatus = MetadataReviewStatus.Reviewed;
            info.MetadataConfidence = 1;
            info.MetadataLastError = null;
            info.MetadataReviewedAt = imported.AppliedAt;
            info.IsAiProcessed = true;
            info.AiRetryCount = 0;
            info.StateVersion = nextVersion;
            info.CurrentMetadataReviewOperationId = operation.Id;
            existingOperationIds.Add(operation.Id);
            statistics.Add();
        }
    }

    private async Task ImportPlaybackAsync(
        LogicalDataBundle bundle,
        LogicalImportConflictStrategy strategy,
        Guid userId,
        ImportStatistics statistics,
        CancellationToken cancellationToken)
    {
        if (!bundle.Categories.HasFlag(LogicalDataCategory.Playback))
            return;

        var paths = bundle.PlaybackProgress.Select(item => item.VirtualPath).Distinct().ToArray();
        var mappings = await context.FileMappings.AsNoTracking()
            .Where(mapping => paths.Contains(mapping.VirtualPath))
            .ToDictionaryAsync(mapping => mapping.VirtualPath, StringComparer.Ordinal, cancellationToken);
        var existing = await context.PlaybackProgresses
            .Where(item => item.UserId == userId && paths.Contains(item.VirtualPath))
            .ToDictionaryAsync(item => item.VirtualPath, StringComparer.Ordinal, cancellationToken);

        foreach (var imported in bundle.PlaybackProgress
                     .GroupBy(item => item.VirtualPath, StringComparer.Ordinal)
                     .Select(group => group.OrderByDescending(item => item.UpdatedAt).First()))
        {
            if (!mappings.TryGetValue(imported.VirtualPath, out var mapping))
            {
                statistics.Skip($"playback path is missing:{imported.VirtualPath}");
                continue;
            }
            if (existing.TryGetValue(imported.VirtualPath, out var progress))
            {
                if (!HandleConflict(strategy, $"playback:{imported.VirtualPath}", statistics))
                    continue;
                ApplyProgress(imported, mapping.AnimationInfoId, userId, progress);
                statistics.Update();
                continue;
            }

            progress = new Models.PlaybackProgress { Id = Guid.NewGuid() };
            ApplyProgress(imported, mapping.AnimationInfoId, userId, progress);
            context.PlaybackProgresses.Add(progress);
            existing.Add(progress.VirtualPath, progress);
            statistics.Add();
        }

        if (bundle.PlaybackPreferences is not { } importedPreferences)
            return;
        var preferences = await context.PlaybackPreferences
            .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (preferences is not null)
        {
            if (!HandleConflict(strategy, "playback-preferences", statistics))
                return;
            ApplyPreferences(importedPreferences, userId, preferences);
            statistics.Update();
            return;
        }

        preferences = new Models.PlaybackPreference();
        ApplyPreferences(importedPreferences, userId, preferences);
        context.PlaybackPreferences.Add(preferences);
        statistics.Add();
    }

    private static bool HandleConflict(
        LogicalImportConflictStrategy strategy,
        string identifier,
        ImportStatistics statistics)
    {
        if (strategy == LogicalImportConflictStrategy.Fail)
            throw new LogicalDataImportConflictException($"Import conflict at {identifier}.");
        if (strategy == LogicalImportConflictStrategy.Skip)
        {
            statistics.Conflict(identifier);
            return false;
        }
        return true;
    }

    private static string Identifier(string kind, string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"{kind}:{Convert.ToHexString(digest.AsSpan(0, 8))}";
    }

    private static void ApplyPolicy(
        LogicalAutomationPolicy source,
        Models.SubscriptionAutomationPolicy target)
    {
        target.SubtitleGroups = source.SubtitleGroups.ToArray();
        target.Resolutions = source.Resolutions.ToArray();
        target.Codecs = source.Codecs.ToArray();
        target.Languages = source.Languages.ToArray();
        target.MinSizeBytes = source.MinSizeBytes;
        target.MaxSizeBytes = source.MaxSizeBytes;
        target.ExcludedKeywords = source.ExcludedKeywords.ToArray();
        target.Mode = source.Mode;
        target.CreatedAt = source.CreatedAt;
        target.UpdatedAt = source.UpdatedAt;
    }

    private static void ApplyProgress(
        LogicalPlaybackProgress source,
        Guid animationInfoId,
        Guid userId,
        Models.PlaybackProgress target)
    {
        target.UserId = userId;
        target.AnimationInfoId = animationInfoId;
        target.VirtualPath = source.VirtualPath;
        target.PositionSeconds = source.PositionSeconds;
        target.DurationSeconds = source.DurationSeconds;
        target.IsWatched = source.IsWatched;
        target.UpdatedAt = source.UpdatedAt;
        target.WatchedAt = source.WatchedAt;
    }

    private static void ApplyPreferences(
        LogicalPlaybackPreferences source,
        Guid userId,
        Models.PlaybackPreference target)
    {
        target.UserId = userId;
        target.SubtitleLanguage = source.SubtitleLanguage;
        target.SubtitleTrackLabel = source.SubtitleTrackLabel;
        target.AudioLanguage = source.AudioLanguage;
        target.AudioTrackLabel = source.AudioTrackLabel;
        target.AutoPlayNext = source.AutoPlayNext;
        target.UpdatedAt = source.UpdatedAt;
    }

    private sealed class ImportStatistics
    {
        private readonly List<string> _messages = [];

        public int Added { get; private set; }
        public int Updated { get; private set; }
        public int Skipped { get; private set; }
        public int Conflicts { get; private set; }

        public void Add() => Added++;
        public void Update() => Updated++;
        public void Skip(string? message = null)
        {
            Skipped++;
            Message(message);
        }

        public void Conflict(string identifier)
        {
            Conflicts++;
            Skipped++;
            Message($"conflict skipped:{identifier}");
        }

        public LogicalImportResult ToResult() =>
            new(Added, Updated, Skipped, Conflicts, _messages);

        private void Message(string? value)
        {
            if (!string.IsNullOrEmpty(value) && _messages.Count < 100)
                _messages.Add(value);
        }
    }
}
