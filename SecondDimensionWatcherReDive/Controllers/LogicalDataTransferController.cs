using SecondDimensionWatcherReDive.Framework.Authorization;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Utils.MetadataReview;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/data-transfer")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Authorize(Policy = AccessPolicies.Administrator)]
internal sealed class LogicalDataTransferController(
    ILogicalDataTransferRepository repository) : ControllerBase
{
    private static readonly string ApplicationVersion =
        typeof(LogicalDataTransferController).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(LogicalDataTransferController).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    [HttpGet("export")]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] string categories = "all",
        CancellationToken cancellationToken = default)
    {
        if (!User.TryGetProfileId(out var profileId)) return Unauthorized();
        if (!TryParseCategories(categories, out var selected))
            return BadRequest(new { error = "Unknown data category." });

        LogicalDataBundle bundle;
        try
        {
            bundle = await repository.ExportAsync(
                selected,
                profileId,
                ApplicationVersion,
                cancellationToken);
        }
        catch (LogicalDataExportLimitException exception)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = exception.Message });
        }

        var digest = Digest(bundle);
        var envelope = new External.LogicalDataExportEnvelope(bundle, digest);
        // Validate the larger import representation, including the longest conflict
        // strategy name, so every successful export fits the import request limit.
        var importBytes = JsonSerializer.SerializeToUtf8Bytes(
            new External.LogicalDataImportRequest(
                bundle,
                digest,
                LogicalImportConflictStrategy.Overwrite),
            External.AppJsonSerializerContext.Default.LogicalDataImportRequest);
        if (importBytes.Length > LogicalDataTransferLimits.MaximumPayloadBytes)
            return StatusCode(
                StatusCodes.Status413PayloadTooLarge,
                new { error = $"Logical export exceeds {LogicalDataTransferLimits.MaximumPayloadBytes} bytes." });
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            External.AppJsonSerializerContext.Default.LogicalDataExportEnvelope);
        var timestamp = bundle.ExportedAtUtc.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        Response.Headers.CacheControl = "private,no-store";
        return File(bytes, "application/json", $"sdw-logical-export-{timestamp}.json");
    }

    [HttpPost("import")]
    [Authorize(Policy = AccessPolicies.RecentAdministrator)]
    [RequestSizeLimit(LogicalDataTransferLimits.MaximumPayloadBytes)]
    public async Task<IActionResult> ImportAsync(
        [FromBody] External.LogicalDataImportRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetProfileId(out var profileId)) return Unauthorized();
        if (request.Data is null || request.ConflictStrategy is null ||
            string.IsNullOrWhiteSpace(request.Sha256))
            return BadRequest(new { error = "Data, sha256 and conflictStrategy are required." });
        if (!Enum.IsDefined(request.ConflictStrategy.Value))
            return BadRequest(new { error = "Unknown import conflict strategy." });

        var bundle = LogicalDataTransferFormat.NormalizeLegacyCategories(request.Data);
        var actualDigest = Encoding.ASCII.GetBytes(Digest(bundle));
        var expectedDigest = Encoding.ASCII.GetBytes(request.Sha256.Trim().ToUpperInvariant());
        if (actualDigest.Length != expectedDigest.Length ||
            !CryptographicOperations.FixedTimeEquals(actualDigest, expectedDigest))
            return BadRequest(new { error = "Logical export checksum mismatch." });
        if (!IsCompatible(bundle, out var error))
            return BadRequest(new { error });

        try
        {
            var result = await repository.ImportAsync(
                bundle,
                request.ConflictStrategy.Value,
                profileId,
                cancellationToken);
            return Ok(result);
        }
        catch (LogicalDataImportConflictException exception)
        {
            return Conflict(new { error = exception.Message });
        }
        catch (MetadataReviewServiceException exception)
        {
            return StatusCode(exception is MetadataReviewUnavailableException ? 503 : 422,
                new External.MetadataReviewError(exception.Code, exception.Message));
        }
    }

    private static bool TryParseCategories(string value, out LogicalDataCategory categories)
    {
        categories = LogicalDataCategory.None;
        foreach (var token in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var category = token.ToLowerInvariant() switch
            {
                "all" => LogicalDataCategory.All,
                "feeds" => LogicalDataCategory.Feeds,
                "automation" or "automation-policies" => LogicalDataCategory.AutomationPolicies,
                "rules" or "filename-rules" => LogicalDataCategory.FileNameRules,
                "recognition-rules" => LogicalDataCategory.RecognitionRules,
                "metadata" or "metadata-corrections" => LogicalDataCategory.MetadataCorrections,
                "playback" => LogicalDataCategory.Playback,
                _ => LogicalDataCategory.None
            };
            if (category == LogicalDataCategory.None)
                return false;
            categories |= category;
        }
        return categories != LogicalDataCategory.None;
    }

    private static bool IsCompatible(LogicalDataBundle bundle, out string error)
    {
        if (string.IsNullOrWhiteSpace(bundle.ApplicationVersion) ||
            bundle.Feeds is null || bundle.AutomationPolicies is null ||
            bundle.FileNameRules is null || bundle.MetadataCorrections is null ||
            bundle.PlaybackProgress is null)
        {
            error = "Logical export is incomplete.";
            return false;
        }
        if (bundle.FormatVersion is not (1 or LogicalDataTransferFormat.CurrentVersion))
        {
            error = $"Unsupported logical export format {bundle.FormatVersion}.";
            return false;
        }
        if (bundle.Categories == LogicalDataCategory.None ||
            (bundle.Categories & ~(bundle.FormatVersion == 1
                ? LogicalDataTransferFormat.LegacyCategories : LogicalDataCategory.All)) != 0 ||
            bundle.FormatVersion == 1 && bundle.RecognitionRules is not null ||
            bundle.FormatVersion == LogicalDataTransferFormat.CurrentVersion && bundle.RecognitionRules is null)
        {
            error = "Logical export contains unknown categories.";
            return false;
        }
        var importedMajor = Major(bundle.ApplicationVersion);
        var currentMajor = Major(ApplicationVersion);
        if (importedMajor < 0 || currentMajor < 0 || importedMajor != currentMajor)
        {
            error = "Logical export was created by an incompatible application major version.";
            return false;
        }
        if (bundle.Feeds.Count > LogicalDataTransferLimits.MaximumItemsPerCategory ||
            bundle.AutomationPolicies.Count > LogicalDataTransferLimits.MaximumItemsPerCategory ||
            bundle.FileNameRules.Count > LogicalDataTransferLimits.MaximumItemsPerCategory ||
            bundle.MetadataCorrections.Count > LogicalDataTransferLimits.MaximumItemsPerCategory ||
            bundle.PlaybackProgress.Count > LogicalDataTransferLimits.MaximumItemsPerCategory)
        {
            error = $"A logical export category exceeds {LogicalDataTransferLimits.MaximumItemsPerCategory} items.";
            return false;
        }
        if (bundle.RecognitionRules?.Count > LogicalDataTransferLimits.MaximumRecognitionRules)
        {
            error = $"A logical export exceeds {LogicalDataTransferLimits.MaximumRecognitionRules} recognition rules.";
            return false;
        }
        if ((!bundle.Categories.HasFlag(LogicalDataCategory.Feeds) && bundle.Feeds.Count > 0) ||
            (!bundle.Categories.HasFlag(LogicalDataCategory.AutomationPolicies) && bundle.AutomationPolicies.Count > 0) ||
            (!bundle.Categories.HasFlag(LogicalDataCategory.FileNameRules) && bundle.FileNameRules.Count > 0) ||
            (!bundle.Categories.HasFlag(LogicalDataCategory.MetadataCorrections) && bundle.MetadataCorrections.Count > 0) ||
            (!bundle.Categories.HasFlag(LogicalDataCategory.RecognitionRules) && bundle.RecognitionRules?.Count > 0) ||
            (!bundle.Categories.HasFlag(LogicalDataCategory.Playback) &&
             (bundle.PlaybackProgress.Count > 0 || bundle.PlaybackPreferences is not null)))
        {
            error = "Logical export data does not match its declared categories.";
            return false;
        }
        if (bundle.PlaybackProgress.Any(item =>
                item.PositionSeconds < 0 || item.DurationSeconds < 0 ||
                !double.IsFinite(item.PositionSeconds) || !double.IsFinite(item.DurationSeconds)))
        {
            error = "Logical export contains invalid playback values.";
            return false;
        }
        if (bundle.RecognitionRules is { } recognitionRules &&
            (recognitionRules.Any(item => item is null || !IsValidRecognitionRule(item)) ||
             recognitionRules.Select(item => item.Id).Distinct().Count() != recognitionRules.Count))
        {
            error = "Logical export contains invalid or duplicate recognition rules.";
            return false;
        }
        if (bundle.Feeds.Any(item => item.Id == Guid.Empty || !IsSafeHttpUrl(item.Url)) ||
            bundle.AutomationPolicies.Any(item =>
                !IsSafeHttpUrl(item.FeedUrl) ||
                item.SubtitleGroups is null || item.Resolutions is null || item.Codecs is null ||
                item.Languages is null || item.ExcludedKeywords is null ||
                !Enum.IsDefined(item.Mode) || item.MinSizeBytes < 0 || item.MaxSizeBytes < 0 ||
                item.MinimumUpgradeScore is < 1 or > 1000 ||
                item.UpgradeRollbackHours is < 1 or > 720 ||
                (item.MinSizeBytes is not null && item.MaxSizeBytes is not null &&
                 item.MinSizeBytes > item.MaxSizeBytes)) ||
            bundle.FileNameRules.Any(item =>
                item.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(item.AnimationTmdbId) ||
                string.IsNullOrWhiteSpace(item.AnimationName) ||
                string.IsNullOrWhiteSpace(item.AnimationOriginalName) ||
                !FileNameRegexMatcher.TryCreateRegex(item.Pattern, out _, out _)) ||
            bundle.FileNameRules
                .GroupBy(item => item.AnimationTmdbId, StringComparer.Ordinal)
                .Any(group => group.Count() > FileNameRegexMatcher.MaxRulesPerAnimation) ||
            bundle.MetadataCorrections.Any(item =>
                item.OperationId == Guid.Empty ||
                string.IsNullOrWhiteSpace(item.ReleaseDownloadUrl) ||
                string.IsNullOrWhiteSpace(item.ReleaseTitle) ||
                string.IsNullOrWhiteSpace(item.AnimationTmdbId) ||
                string.IsNullOrWhiteSpace(item.AnimationName) ||
                string.IsNullOrWhiteSpace(item.AnimationOriginalName) ||
                item.Description is null || item.Season < 0 || item.Episode < 0) ||
            bundle.PlaybackProgress.Any(item =>
                string.IsNullOrWhiteSpace(item.VirtualPath) ||
                !item.VirtualPath.StartsWith("/", StringComparison.Ordinal)) ||
            bundle.PlaybackPreferences is { } preferences &&
            (preferences.SubtitleLanguage?.Length > 64 ||
             preferences.AudioLanguage?.Length > 64 ||
             preferences.SubtitleTrackLabel?.Length > 128 ||
             preferences.AudioTrackLabel?.Length > 128))
        {
            error = "Logical export contains invalid identifiers or paths.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool IsSafeHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsValidRecognitionRule(LogicalRecognitionRule rule)
    {
        if (rule.Id == Guid.Empty || string.IsNullOrWhiteSpace(rule.Name) || rule.Name.Length > 200 ||
            rule.TitlePattern?.Length > 1000 || rule.SubtitleGroup?.Length > 200 ||
            rule.CanonicalGroupName?.Length > 200 || rule.FixedSeason < 0 ||
            rule.EpisodeOffset is < -10000 or > 10000 ||
            !int.TryParse(rule.TmdbId, NumberStyles.None, CultureInfo.InvariantCulture, out var tmdbId) || tmdbId <= 0 ||
            rule.SourceFeedUrl is not null && !IsSafeHttpUrl(rule.SourceFeedUrl) ||
            rule.SourceFeedMissing && rule.SourceFeedUrl is not null ||
            rule.SourceFeedUrl is null && !rule.SourceFeedMissing &&
                string.IsNullOrWhiteSpace(rule.TitlePattern) && string.IsNullOrWhiteSpace(rule.SubtitleGroup))
            return false;
        if (rule.TitlePattern is null) return true;
        var pattern = rule.TitlePattern.Trim();
        if (pattern.Length == 0) return false;
        try
        {
            var regex = new Regex(pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            return !regex.IsMatch("");
        }
        catch (ArgumentException) { return false; }
        catch (RegexMatchTimeoutException) { return false; }
    }

    private static int Major(string version)
    {
        var value = version.Split('+', 2)[0].Split('-', 2)[0];
        return Version.TryParse(value, out var parsed) ? parsed.Major : -1;
    }

    private static string Digest(LogicalDataBundle bundle)
    {
        bundle = LogicalDataTransferFormat.NormalizeLegacyCategories(bundle);
        var bytes = bundle.FormatVersion == 1
            ? JsonSerializer.SerializeToUtf8Bytes(new External.LogicalDataBundleV1(
                bundle.FormatVersion, bundle.ExportedAtUtc, bundle.ApplicationVersion,
                (External.LogicalDataCategoryV1)bundle.Categories, bundle.Feeds, bundle.AutomationPolicies,
                bundle.FileNameRules, bundle.MetadataCorrections, bundle.PlaybackProgress, bundle.PlaybackPreferences),
                External.AppJsonSerializerContext.Default.LogicalDataBundleV1)
            : JsonSerializer.SerializeToUtf8Bytes(bundle,
                External.AppJsonSerializerContext.Default.LogicalDataBundle);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
