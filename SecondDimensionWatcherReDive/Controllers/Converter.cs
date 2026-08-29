using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.Plugin;

namespace SecondDimensionWatcherReDive.Controllers;

internal static class Converter
{
    public static External.PluginCapabilities ToExternal(this PluginCapabilities capabilities) =>
        new(capabilities.NetworkDomains,
            capabilities.FileRoots,
            capabilities.Notifications,
            capabilities.DownloadControl,
            capabilities.StorageAccess,
            capabilities.BackgroundTasks);

    public static PluginCapabilities ToDomain(this External.PluginCapabilities capabilities) =>
        new()
        {
            NetworkDomains = capabilities.NetworkDomains,
            FileRoots = capabilities.FileRoots,
            Notifications = capabilities.Notifications,
            DownloadControl = capabilities.DownloadControl,
            StorageAccess = capabilities.StorageAccess,
            BackgroundTasks = capabilities.BackgroundTasks
        };

    public static External.PluginManifest ToExternal(this PluginManifest manifest) =>
        new(manifest.Id,
            manifest.Name,
            manifest.Version,
            manifest.ApiVersion,
            manifest.EntryPoint,
            manifest.Description,
            manifest.Dependencies.Select(dependency =>
                new External.PluginDependency(dependency.Id, dependency.MinimumVersion)).ToArray(),
            manifest.Capabilities.ToExternal(),
            manifest.Platforms,
            manifest.Integrity?.Files ?? new Dictionary<string, string>(),
            manifest.Signature?.Publisher,
            manifest.Signature?.Algorithm,
            manifest.Providers.Select(provider => new External.PluginProvider(
                provider.Kind,
                provider.Name,
                provider.Handlers)).ToArray(),
            manifest.DataVersion,
            manifest.DataMigration is null
                ? null
                : new External.PluginDataMigration(
                    manifest.DataMigration.Strategy,
                    manifest.DataMigration.Description));

    public static External.PluginHealth ToExternal(this PluginHealth health) =>
        new(health.Status,
            health.ConsecutiveFailures,
            health.LastSuccessAt,
            health.LastFailureAt,
            health.LastError,
            health.CircuitOpenUntil);

    public static External.InstalledPlugin ToExternal(this InstalledPlugin plugin) =>
        new(plugin.Manifest.ToExternal(),
            plugin.IsEnabled,
            plugin.ApprovedCapabilities.ToExternal(),
            plugin.CompatibilityErrors,
            plugin.Health.ToExternal(),
            plugin.Configuration.ValueKind == System.Text.Json.JsonValueKind.Object &&
            plugin.Configuration.EnumerateObject().Any());

    public static External.PluginPackagePreview ToExternal(this PluginPackagePreview preview) =>
        new(preview.Token,
            preview.PackageSha256,
            preview.Manifest.ToExternal(),
            preview.CompatibilityErrors,
            preview.IsSignatureTrusted,
            preview.SignatureStatus,
            preview.ExpiresAt);

    public static External.PluginInstallResult ToExternal(this PluginInstallResult result) =>
        new(result.Id, result.Version, result.IsUpgrade, result.CompatibilityErrors);

    public static External.AnimationInfo ToExternal(this AnimationInfo record) =>
        new(record.Id,
            record.Title,
            record.Description,
            record.PublishTime,
            record.IsDownloadTracked,
            record.IsDownloadFinished,
            record.Season,
            record.Episode,
            record.Group?.ToExternal(),
            record.Animation?.ToExternal(),
            record.IsAiProcessed,
            record.SourceFeedId,
            record.ReleaseSizeBytes,
            record.AutomationDisposition?.ToString(),
            record.AutomationExplanationJson,
            string.Equals(
                record.DownloadType,
                FileDownloadTypes.MediaLibraryImport,
                StringComparison.Ordinal));

    public static External.Animation ToExternal(this Animation record) =>
        new(record.Name,
            record.OriginalName,
            record.TmdbId,
            record.PosterPath);

    public static External.AnimationGroup ToExternal(this AnimationGroup record) =>
        new(record.Name);

    public static External.AnimationGroupedResponse ToExternal(this AnimationGroupedResult result) =>
        new(result.Animations.Select(a => a.ToExternal()).ToList(),
            result.Uncategorized.Select(i => i.ToExternal()).ToList());

    public static External.AnimationWithEpisodes ToExternal(this AnimationWithEpisodesResult result) =>
        new(result.TmdbId,
            result.Name,
            result.OriginalName,
            result.PosterPath,
            result.EpisodeCount,
            result.Episodes.Select(e => e.ToExternal()).ToList());

    public static External.Feed ToExternal(this Feed record) =>
        new(record.Id, record.Url, record.Name, record.CreatedAt);

    public static External.SubscriptionAutomationPolicy ToExternal(
        this SubscriptionAutomationPolicy record) =>
        new(record.FeedId,
            record.SubtitleGroups,
            record.Resolutions,
            record.Codecs,
            record.Languages,
            record.MinSizeBytes,
            record.MaxSizeBytes,
            record.ExcludedKeywords,
            record.Mode.ToString(),
            record.CreatedAt,
            record.UpdatedAt);

    public static External.SubscriptionAutomationSimulationResult ToExternal(
        this Framework.Feed.SubscriptionAutomationSimulationResult result) =>
        new(result.Total,
            result.Matched,
            result.Entries.Select(entry => new External.SubscriptionAutomationSimulationEntry(
                entry.Id,
                entry.Title,
                entry.PublishedAt,
                entry.SizeBytes,
                entry.Matched,
                entry.Explanations.Select(explanation =>
                    new External.SubscriptionAutomationExplanation(
                        explanation.Field,
                        explanation.Passed,
                        explanation.Actual,
                        explanation.Expected,
                        explanation.Message)).ToList())).ToList());

    public static External.WebDavTokenSummary ToExternal(this WebDavToken record) =>
        new(record.Id, record.Username, record.Description, record.CreatedAt);

    public static External.SeasonBangumi ToExternal(this SeasonBangumi record) =>
        new(record.Id,
            record.MikanId,
            record.Title,
            record.DayOfWeek,
            record.ImageUrl,
            record.ScrapedAt);

    public static External.FileDownloadStatus ToExternal(this Data.FileDownloadStatus status) =>
        new(status.ItemId,
            status.Progress,
            status.Remaining,
            status.Speed,
            status.State);

    public static External.ResponseData<IEnumerable<External.AnimationInfo>> ToExternalResponseData(
        this IReadOnlyList<AnimationInfo> data, int totalCount) =>
        new(data.Select(d => d.ToExternal()), totalCount);

    public static External.ResponseData<List<External.AnimationInfo>> ToExternalListResponseData(
        this IReadOnlyList<AnimationInfo> data, int totalCount) =>
        new(data.Select(d => d.ToExternal()).ToList(), totalCount);
}
