using System.Text.RegularExpressions;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Inference.AI.Tools;
using SecondDimensionWatcherReDive.Utils.FileStore;
using SecondDimensionWatcherReDive.Utils.Incidents;

namespace SecondDimensionWatcherReDive.Utils.MetadataReview;

public interface IMetadataReviewService
{
    Task<MetadataReviewPreviewResult> PreviewAsync(
        Guid animationInfoId,
        long expectedRevision,
        MetadataReviewCorrection correction,
        CancellationToken cancellationToken);

    Task<MetadataReviewChangeResult> ApplyAsync(
        Guid animationInfoId,
        Guid previewId,
        CancellationToken cancellationToken);

    Task<MetadataReviewChangeResult> UndoAsync(
        Guid operationId,
        long expectedRevision,
        CancellationToken cancellationToken);
}

public sealed record MetadataReviewCorrection(
    string? TmdbId,
    int? Season,
    int? Episode,
    string? GroupName);

public sealed record MetadataReviewResolvedMetadata(
    string TmdbId,
    string Name,
    string OriginalName,
    string? PosterPath,
    int Season,
    int? Episode,
    string? GroupName);

public sealed record MetadataReviewPathChange(
    string FileName,
    string? CurrentVirtualPath,
    string? ProposedVirtualPath,
    string ChangeKind,
    bool CollisionAdjusted);

public sealed record MetadataReviewPreviewResult(
    Guid PreviewId,
    long BaseRevision,
    MetadataReviewResolvedMetadata ResolvedMetadata,
    IReadOnlyList<MetadataReviewPathChange> PathChanges,
    IReadOnlyList<string> Warnings,
    bool CanApply,
    DateTimeOffset ExpiresAt);

public sealed record MetadataReviewChangeResult(
    Guid OperationId,
    long Revision,
    IReadOnlyList<MetadataReviewPathChange> PathChanges,
    DateTimeOffset AppliedAt,
    bool CanUndo);

public abstract class MetadataReviewServiceException(string code, string message)
    : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class MetadataReviewNotFoundException(string code, string message)
    : MetadataReviewServiceException(code, message);

public sealed class MetadataReviewConflictException(string code, string message)
    : MetadataReviewServiceException(code, message);

public sealed class MetadataReviewValidationException(string code, string message)
    : MetadataReviewServiceException(code, message);

public sealed class MetadataReviewUnavailableException(string code, string message)
    : MetadataReviewServiceException(code, message);

public sealed partial class MetadataReviewService(
    IAnimationInfoRepository animationInfoRepository,
    IAnimationRepository animationRepository,
    IAnimationGroupRepository animationGroupRepository,
    IFileMappingRepository fileMappingRepository,
    IMetadataReviewRepository metadataReviewRepository,
    IFileMapper fileMapper,
    TmdbTool tmdbTool,
    IIncidentReporter? incidentReporter = null) : IMetadataReviewService
{
    private static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(15);
    private const int MaxGroupNameLength = 200;

    public async Task<MetadataReviewPreviewResult> PreviewAsync(
        Guid animationInfoId,
        long expectedRevision,
        MetadataReviewCorrection correction,
        CancellationToken cancellationToken)
    {
        var current = await animationInfoRepository.FindByIdWithAnimationAsync(
            animationInfoId,
            cancellationToken);
        if (current is null)
            throw new MetadataReviewNotFoundException("itemNotFound", "The metadata item was not found.");
        if (current.StateVersion != expectedRevision)
            throw new MetadataReviewConflictException(
                "staleRevision",
                "The item changed after it was loaded. Refresh it before previewing again.");

        var tmdbId = ValidateTmdbId(correction.TmdbId);
        var season = correction.Season
                     ?? throw new MetadataReviewValidationException(
                         "seasonRequired",
                         "A TMDB season is required.");
        if (season < 0)
            throw new MetadataReviewValidationException("invalidSeason", "Season cannot be negative.");
        if (correction.Episode is < 0)
            throw new MetadataReviewValidationException("invalidEpisode", "Episode cannot be negative.");

        var groupName = NormalizeGroupName(correction.GroupName);
        var animation = await ResolveAnimationAsync(tmdbId, cancellationToken);
        var group = groupName is null
            ? null
            : await animationGroupRepository.FindByNameAsync(groupName, cancellationToken)
              ?? new AnimationGroup(Guid.NewGuid(), groupName);

        var proposed = current with
        {
            Animation = animation,
            Group = group,
            Season = season,
            Episode = correction.Episode,
            IsAiProcessed = true,
            AiRetryCount = 0,
            MetadataStatus = MetadataReviewStatus.Reviewed,
            MetadataConfidence = 1,
            MetadataLastError = null,
            MetadataReviewedAt = DateTimeOffset.UtcNow
        };

        var currentMappings = await fileMappingRepository.GetForAnimationInfoAsync(
            animationInfoId,
            cancellationToken);
        IReadOnlyList<FileMapping> proposedMappings;
        var warnings = new List<string>();
        if (current.IsDownloadFinished)
        {
            if (current.FileStore is null || current.StorePath is null)
                throw new MetadataReviewConflictException(
                    "downloadLocationMissing",
                    "The downloaded item no longer has a valid storage location.");

            var preview = await fileMapper.PreviewDownloadAsync(proposed, cancellationToken);
            if (preview is null)
                throw new MetadataReviewUnavailableException(
                    "mappingUnavailable",
                    "The downloaded files could not be enumerated for path preview.");
            proposedMappings = preview.Mappings;
            warnings.AddRange(preview.Warnings);
        }
        else
        {
            // A metadata-only correction must not unexpectedly delete mappings if an
            // inconsistent legacy row happens to have them before download completion.
            proposedMappings = currentMappings;
            warnings.Add("notDownloaded");
        }

        var now = DateTimeOffset.UtcNow;
        var operationId = Guid.NewGuid();
        var expiresAt = now.Add(PreviewLifetime);
        var proposedDescription = await ResolveDescriptionAsync(
            tmdbId,
            animation,
            current.Description,
            cancellationToken);
        await metadataReviewRepository.SavePreviewAsync(
            new MetadataReviewPreviewDraft(
                operationId,
                animationInfoId,
                current.StateVersion,
                current.FileStore,
                current.StorePath,
                current.IsDownloadFinished,
                animation,
                proposedDescription,
                season,
                correction.Episode,
                groupName,
                now,
                expiresAt,
                proposedMappings),
            cancellationToken);

        return new MetadataReviewPreviewResult(
            operationId,
            current.StateVersion,
            new MetadataReviewResolvedMetadata(
                animation.TmdbId,
                animation.Name,
                animation.OriginalName,
                animation.PosterPath,
                season,
                correction.Episode,
                groupName),
            BuildPathChanges(currentMappings, proposedMappings),
            warnings.Distinct(StringComparer.Ordinal).ToList(),
            true,
            expiresAt);
    }

    public async Task<MetadataReviewChangeResult> ApplyAsync(
        Guid animationInfoId,
        Guid previewId,
        CancellationToken cancellationToken)
    {
        var result = await metadataReviewRepository.ApplyPreviewAsync(
            previewId,
            animationInfoId,
            cancellationToken);
        var change = ToChangeResult(result, applying: true);
        if (incidentReporter is not null)
            await incidentReporter.ResolveAsync(
                IncidentType.FileMappingFailure,
                animationInfoId.ToString(),
                cancellationToken);
        return change;
    }

    public async Task<MetadataReviewChangeResult> UndoAsync(
        Guid operationId,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        var result = await metadataReviewRepository.UndoAsync(
            operationId,
            expectedRevision,
            cancellationToken);
        return ToChangeResult(result, applying: false);
    }

    private async Task<Animation> ResolveAnimationAsync(
        string tmdbId,
        CancellationToken cancellationToken)
    {
        var existing = await animationRepository.FindByTmdbIdAsync(tmdbId, cancellationToken);
        if (existing is not null) return existing;

        if (!tmdbTool.IsConfigured)
            throw new MetadataReviewUnavailableException(
                "tmdbUnavailable",
                "TMDB lookup is unavailable because no API key is configured.");

        var details = await tmdbTool.GetLocalizedDetailsAsync(
            int.Parse(tmdbId, System.Globalization.CultureInfo.InvariantCulture),
            cancellationToken);
        if (details is null || string.IsNullOrWhiteSpace(details.Name))
            throw new MetadataReviewValidationException(
                "tmdbNotFound",
                "The TMDB television series could not be resolved.");

        return new Animation(
            Guid.NewGuid(),
            tmdbId,
            details.Name,
            details.OriginalName,
            details.PosterPath);
    }

    private async Task<string> ResolveDescriptionAsync(
        string tmdbId,
        Animation animation,
        string fallback,
        CancellationToken cancellationToken)
    {
        // Existing Animation records do not retain the localized overview, so resolve it
        // here as part of the preview. A failed optional lookup keeps the current text.
        var details = await tmdbTool.GetLocalizedDetailsAsync(
            int.Parse(tmdbId, System.Globalization.CultureInfo.InvariantCulture),
            cancellationToken);
        _ = animation;
        return string.IsNullOrWhiteSpace(details?.Overview) ? fallback : details.Overview;
    }

    private static MetadataReviewChangeResult ToChangeResult(
        MetadataReviewMutationResult result,
        bool applying)
    {
        switch (result.Outcome)
        {
            case MetadataReviewMutationOutcome.NotFound:
                throw new MetadataReviewNotFoundException(
                    "operationNotFound",
                    "The metadata review operation was not found.");
            case MetadataReviewMutationOutcome.Expired:
                throw new MetadataReviewConflictException(
                    "previewExpired",
                    "The path preview expired. Create a fresh preview before applying it.");
            case MetadataReviewMutationOutcome.Conflict:
                throw new MetadataReviewConflictException(
                    applying ? "stalePreview" : "undoConflict",
                    applying
                        ? "The item or its virtual paths changed after preview. Preview it again."
                        : "This operation can no longer be undone because the item changed.");
            case MetadataReviewMutationOutcome.Success:
                break;
            default:
                throw new InvalidOperationException($"Unknown metadata mutation outcome: {result.Outcome}");
        }

        if (result.Revision is null || result.AppliedAt is null)
            throw new InvalidOperationException("A successful metadata review mutation returned no revision.");

        return new MetadataReviewChangeResult(
            result.OperationId,
            result.Revision.Value,
            BuildPathChanges(result.MappingsBefore, result.MappingsAfter),
            result.AppliedAt.Value,
            applying);
    }

    private static IReadOnlyList<MetadataReviewPathChange> BuildPathChanges(
        IReadOnlyList<FileMapping> before,
        IReadOnlyList<FileMapping> after)
    {
        var currentByPhysicalPath = before
            .GroupBy(mapping => mapping.PhysicalPath, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var proposedByPhysicalPath = after
            .GroupBy(mapping => mapping.PhysicalPath, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var physicalPaths = currentByPhysicalPath.Keys
            .Concat(proposedByPhysicalPath.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal);

        var changes = new List<MetadataReviewPathChange>();
        foreach (var physicalPath in physicalPaths)
        {
            currentByPhysicalPath.TryGetValue(physicalPath, out var current);
            proposedByPhysicalPath.TryGetValue(physicalPath, out var proposed);
            var kind = current is null
                ? "added"
                : proposed is null
                    ? "removed"
                    : string.Equals(current.VirtualPath, proposed.VirtualPath, StringComparison.Ordinal)
                        ? "unchanged"
                        : "moved";
            changes.Add(new MetadataReviewPathChange(
                Path.GetFileName(physicalPath),
                current?.VirtualPath,
                proposed?.VirtualPath,
                kind,
                proposed is not null && CollisionSuffixRegex().IsMatch(proposed.VirtualPath)));
        }

        return changes;
    }

    private static string ValidateTmdbId(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)
            || !int.TryParse(normalized, out var parsed)
            || parsed <= 0)
            throw new MetadataReviewValidationException(
                "invalidTmdbId",
                "TMDB ID must be a positive integer.");
        return parsed.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string? NormalizeGroupName(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > MaxGroupNameLength)
            throw new MetadataReviewValidationException(
                "groupNameTooLong",
                $"Subtitle group cannot exceed {MaxGroupNameLength} characters.");
        return normalized;
    }

    [GeneratedRegex(@" \(\d+\)(?=\.[^/]+$|$)", RegexOptions.CultureInvariant)]
    private static partial Regex CollisionSuffixRegex();
}
