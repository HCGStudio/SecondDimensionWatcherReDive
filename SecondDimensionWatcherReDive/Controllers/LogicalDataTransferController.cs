using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Inference;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/data-transfer")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed class LogicalDataTransferController(
    ILogicalDataTransferRepository repository) : ControllerBase
{
    private const int SupportedFormatVersion = 1;
    private const int MaximumItemsPerCategory = 10_000;
    private static readonly Guid CurrentUserId = Guid.Empty;
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
        if (!TryParseCategories(categories, out var selected))
            return BadRequest(new { error = "Unknown data category." });

        var bundle = await repository.ExportAsync(
            selected,
            CurrentUserId,
            ApplicationVersion,
            cancellationToken);
        var envelope = new External.LogicalDataExportEnvelope(bundle, Digest(bundle));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            External.AppJsonSerializerContext.Default.LogicalDataExportEnvelope);
        var timestamp = bundle.ExportedAtUtc.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        Response.Headers.CacheControl = "private,no-store";
        return File(bytes, "application/json", $"sdw-logical-export-{timestamp}.json");
    }

    [HttpPost("import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> ImportAsync(
        [FromBody] External.LogicalDataImportRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Data is null || request.ConflictStrategy is null ||
            string.IsNullOrWhiteSpace(request.Sha256))
            return BadRequest(new { error = "Data, sha256 and conflictStrategy are required." });
        if (!Enum.IsDefined(request.ConflictStrategy.Value))
            return BadRequest(new { error = "Unknown import conflict strategy." });

        var actualDigest = Encoding.ASCII.GetBytes(Digest(request.Data));
        var expectedDigest = Encoding.ASCII.GetBytes(request.Sha256.Trim().ToUpperInvariant());
        if (actualDigest.Length != expectedDigest.Length ||
            !CryptographicOperations.FixedTimeEquals(actualDigest, expectedDigest))
            return BadRequest(new { error = "Logical export checksum mismatch." });
        if (!IsCompatible(request.Data, out var error))
            return BadRequest(new { error });

        try
        {
            var result = await repository.ImportAsync(
                request.Data,
                request.ConflictStrategy.Value,
                CurrentUserId,
                cancellationToken);
            return Ok(result);
        }
        catch (LogicalDataImportConflictException exception)
        {
            return Conflict(new { error = exception.Message });
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
        if (bundle.FormatVersion != SupportedFormatVersion)
        {
            error = $"Unsupported logical export format {bundle.FormatVersion}.";
            return false;
        }
        if (bundle.Categories == LogicalDataCategory.None ||
            (bundle.Categories & ~LogicalDataCategory.All) != 0)
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
        if (bundle.Feeds.Count > MaximumItemsPerCategory ||
            bundle.AutomationPolicies.Count > MaximumItemsPerCategory ||
            bundle.FileNameRules.Count > MaximumItemsPerCategory ||
            bundle.MetadataCorrections.Count > MaximumItemsPerCategory ||
            bundle.PlaybackProgress.Count > MaximumItemsPerCategory)
        {
            error = $"A logical export category exceeds {MaximumItemsPerCategory} items.";
            return false;
        }
        if ((!bundle.Categories.HasFlag(LogicalDataCategory.Feeds) && bundle.Feeds.Count > 0) ||
            (!bundle.Categories.HasFlag(LogicalDataCategory.AutomationPolicies) && bundle.AutomationPolicies.Count > 0) ||
            (!bundle.Categories.HasFlag(LogicalDataCategory.FileNameRules) && bundle.FileNameRules.Count > 0) ||
            (!bundle.Categories.HasFlag(LogicalDataCategory.MetadataCorrections) && bundle.MetadataCorrections.Count > 0) ||
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
        if (bundle.Feeds.Any(item => item.Id == Guid.Empty || !IsSafeHttpUrl(item.Url)) ||
            bundle.AutomationPolicies.Any(item =>
                !IsSafeHttpUrl(item.FeedUrl) ||
                item.SubtitleGroups is null || item.Resolutions is null || item.Codecs is null ||
                item.Languages is null || item.ExcludedKeywords is null ||
                !Enum.IsDefined(item.Mode) || item.MinSizeBytes < 0 || item.MaxSizeBytes < 0 ||
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

    private static int Major(string version)
    {
        var value = version.Split('+', 2)[0].Split('-', 2)[0];
        return Version.TryParse(value, out var parsed) ? parsed.Major : -1;
    }

    private static string Digest(LogicalDataBundle bundle)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            bundle,
            External.AppJsonSerializerContext.Default.LogicalDataBundle);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
