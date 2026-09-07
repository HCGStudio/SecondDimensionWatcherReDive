using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.MetadataReview;
using External = SecondDimensionWatcherReDive.Controllers.External;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/metadata-review")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed class MetadataReviewController(
    IMetadataReviewRepository metadataReviewRepository,
    IMetadataReviewService metadataReviewService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAsync(
        [FromQuery] string status = "pending",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        [FromQuery] Guid? focus = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseQueueStatus(status, out var parsedStatus))
            return BadRequest(new External.MetadataReviewError(
                "invalidStatus",
                "Status must be pending, lowConfidence, or failed."));
        if (skip < 0 || take is < 1 or > 100)
            return BadRequest(new External.MetadataReviewError(
                "invalidPagination",
                "Skip must be non-negative and take must be between 1 and 100."));

        var page = await metadataReviewRepository.GetQueueAsync(
            parsedStatus,
            skip,
            take,
            focus,
            cancellationToken);
        return Ok(ToExternal(page));
    }

    [HttpPost("{id:guid}/preview")]
    public async Task<IActionResult> PreviewAsync(
        [FromRoute] Guid id,
        [FromBody] External.MetadataReviewPreviewRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Metadata is null)
            return UnprocessableEntity(new External.MetadataReviewError(
                "metadataRequired",
                "Metadata values are required."));

        try
        {
            var result = await metadataReviewService.PreviewAsync(
                id,
                request.ExpectedRevision,
                new MetadataReviewCorrection(
                    request.Metadata.TmdbId,
                    request.Metadata.Season,
                    request.Metadata.Episode,
                    request.Metadata.GroupName),
                cancellationToken);
            return Ok(ToExternal(result));
        }
        catch (MetadataReviewServiceException ex)
        {
            return ToErrorResult(ex);
        }
    }

    [HttpPost("{id:guid}/apply")]
    public async Task<IActionResult> ApplyAsync(
        [FromRoute] Guid id,
        [FromBody] External.MetadataReviewApplyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await metadataReviewService.ApplyAsync(
                id,
                request.PreviewId,
                cancellationToken);
            return Ok(ToExternal(result));
        }
        catch (MetadataReviewServiceException ex)
        {
            return ToErrorResult(ex);
        }
    }

    [HttpPost("remaps/{operationId:guid}/undo")]
    public async Task<IActionResult> UndoAsync(
        [FromRoute] Guid operationId,
        [FromBody] External.MetadataReviewUndoRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await metadataReviewService.UndoAsync(
                operationId,
                request.ExpectedRevision,
                cancellationToken);
            return Ok(ToExternal(result));
        }
        catch (MetadataReviewServiceException ex)
        {
            return ToErrorResult(ex);
        }
    }

    private IActionResult ToErrorResult(MetadataReviewServiceException exception)
    {
        var error = new External.MetadataReviewError(exception.Code, exception.Message);
        return exception switch
        {
            MetadataReviewNotFoundException => NotFound(error),
            MetadataReviewConflictException => Conflict(error),
            MetadataReviewValidationException => UnprocessableEntity(error),
            MetadataReviewUnavailableException => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                error),
            _ => StatusCode(StatusCodes.Status500InternalServerError, error)
        };
    }

    private static External.MetadataReviewQueueResponse ToExternal(MetadataReviewQueuePage page) =>
        new(
            page.Data.Select(ToExternal).ToList(),
            page.TotalCount,
            new External.MetadataReviewCounts(
                page.Counts.Pending,
                page.Counts.LowConfidence,
                page.Counts.Failed),
            page.RecentOperations.Select(operation =>
                new External.MetadataReviewRecentOperation(
                    operation.OperationId,
                    operation.AnimationInfoId,
                    operation.Title,
                    operation.AppliedAt,
                    operation.Revision,
                    operation.CanUndo)).ToList());

    private static External.MetadataReviewItem ToExternal(MetadataReviewQueueItem item)
    {
        var info = item.Info;
        return new External.MetadataReviewItem(
            info.Id,
            info.Title,
            info.Description,
            info.PublishTime,
            ToExternalStatus(info.MetadataStatus),
            info.MetadataConfidence,
            info.MetadataLastError,
            info.AiRetryCount,
            new External.MetadataReviewMetadata(
                info.Animation?.TmdbId,
                info.Animation?.Name,
                info.Animation?.OriginalName,
                info.Animation?.PosterPath,
                info.Season,
                info.Episode,
                info.Group?.Name),
            info.IsDownloadFinished,
            item.MappedFileCount,
            info.StateVersion,
            item.CurrentOperationId,
            item.CurrentOperationAppliedAt,
            item.CanUndo);
    }

    private static External.MetadataReviewPreviewResponse ToExternal(
        MetadataReviewPreviewResult result) =>
        new(
            result.PreviewId,
            result.BaseRevision,
            new External.MetadataReviewMetadata(
                result.ResolvedMetadata.TmdbId,
                result.ResolvedMetadata.Name,
                result.ResolvedMetadata.OriginalName,
                result.ResolvedMetadata.PosterPath,
                result.ResolvedMetadata.Season,
                result.ResolvedMetadata.Episode,
                result.ResolvedMetadata.GroupName),
            result.PathChanges.Select(ToExternal).ToList(),
            result.Warnings,
            result.CanApply,
            result.ExpiresAt);

    private static External.MetadataReviewMutationResponse ToExternal(
        MetadataReviewChangeResult result) =>
        new(
            result.OperationId,
            result.Revision,
            result.PathChanges.Select(ToExternal).ToList(),
            result.AppliedAt,
            result.CanUndo);

    private static External.MetadataReviewPathChange ToExternal(MetadataReviewPathChange change) =>
        new(
            change.FileName,
            change.CurrentVirtualPath,
            change.ProposedVirtualPath,
            change.ChangeKind,
            change.CollisionAdjusted);

    private static bool TryParseQueueStatus(string value, out MetadataReviewStatus status)
    {
        if (value.Equals("pending", StringComparison.OrdinalIgnoreCase))
        {
            status = MetadataReviewStatus.Pending;
            return true;
        }
        if (value.Equals("lowConfidence", StringComparison.OrdinalIgnoreCase)
            || value.Equals("low-confidence", StringComparison.OrdinalIgnoreCase))
        {
            status = MetadataReviewStatus.LowConfidence;
            return true;
        }
        if (value.Equals("failed", StringComparison.OrdinalIgnoreCase))
        {
            status = MetadataReviewStatus.Failed;
            return true;
        }

        status = default;
        return false;
    }

    private static string ToExternalStatus(MetadataReviewStatus status) => status switch
    {
        MetadataReviewStatus.Pending => "pending",
        MetadataReviewStatus.LowConfidence => "lowConfidence",
        MetadataReviewStatus.Failed => "failed",
        MetadataReviewStatus.Identified => "identified",
        MetadataReviewStatus.Reviewed => "reviewed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
