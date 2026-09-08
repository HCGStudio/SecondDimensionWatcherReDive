using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.MetadataReview;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/metadata-rules")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Authorize(Policy = AccessPolicies.Administrator)]
internal sealed class MetadataRecognitionRulesController(
    IMetadataRecognitionRuleRepository repository,
    MetadataRecognitionRuleService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken) =>
        Ok(await repository.ListAsync(cancellationToken));

    [HttpGet("hits")]
    public async Task<IActionResult> HitsAsync([FromQuery] Guid? itemId, CancellationToken cancellationToken) =>
        Ok(await repository.GetHitsAsync(itemId, cancellationToken));

    [HttpGet("from-correction/{itemId:guid}")]
    public Task<IActionResult> SeedAsync(Guid itemId, CancellationToken cancellationToken) =>
        RespondAsync(async () => await service.GetSeedAsync(itemId, cancellationToken));

    [HttpPost]
    public Task<IActionResult> CreateAsync([FromBody] MetadataRecognitionRuleDraft request,
        CancellationToken cancellationToken) =>
        RespondAsync(async () => await service.SaveAsync(request, null, cancellationToken));

    [HttpPut("{id:guid}")]
    public Task<IActionResult> EditAsync(Guid id, [FromBody] MetadataRecognitionRuleDraft request,
        CancellationToken cancellationToken) =>
        RespondAsync(async () => await service.SaveAsync(request, id, cancellationToken));

    [HttpPost("preview")]
    public Task<IActionResult> PreviewAsync([FromBody] MetadataRecognitionRuleDraft request,
        [FromQuery] Guid? id, CancellationToken cancellationToken) =>
        RespondAsync(async () => await service.PreviewAsync(request, id, cancellationToken));

    [HttpPost("{id:guid}/history/{itemId:guid}/preview")]
    public Task<IActionResult> PreviewHistoryAsync(Guid id, Guid itemId,
        [FromBody] External.MetadataRecognitionHistoryRequest request, CancellationToken cancellationToken) =>
        RespondAsync(async () => MetadataReviewController.ToExternal(await service.PreviewHistoryAsync(
            id, itemId, request.RuleRevision, request.ItemRevision, cancellationToken)));

    private async Task<IActionResult> RespondAsync(Func<Task<object>> action)
    {
        try { return Ok(await action()); }
        catch (MetadataReviewServiceException exception)
        {
            var error = new External.MetadataReviewError(exception.Code, exception.Message);
            return exception switch
            {
                MetadataReviewNotFoundException => NotFound(error),
                MetadataReviewConflictException => Conflict(error),
                MetadataReviewUnavailableException => StatusCode(503, error),
                _ => UnprocessableEntity(error)
            };
        }
        catch (MetadataRecognitionAmbiguousException exception)
        {
            return UnprocessableEntity(new External.MetadataReviewError("ruleAmbiguous", exception.Message));
        }
    }
}
