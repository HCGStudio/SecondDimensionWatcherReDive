using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.LibraryCompletion;
namespace SecondDimensionWatcherReDive.Controllers;

[ApiController, Route("api/multi-source-subscriptions")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed class MultiSourceSubscriptionsController(IMultiSourceSubscriptionRepository repository,
    MultiSourceCoordinator coordinator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken)
    {
        var result = new List<MultiSourceSubscriptionStatus>();
        foreach (var subscription in await repository.GetAllAsync(cancellationToken))
            result.Add(new(subscription, await repository.GetSourceStatusAsync(subscription.Id, cancellationToken),
                await repository.GetDecisionsAsync(subscription.Id, cancellationToken)));
        return Ok(result);
    }
    [HttpPut("{id:guid}"), Authorize(Policy = AccessPolicies.ContentWrite)]
    public async Task<IActionResult> SaveAsync(Guid id, MultiSourceSubscription input, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(input.Name) || input.Name is not { Length: > 0 and <= 200 } || !int.TryParse(input.TmdbId, out var tmdb) || tmdb <= 0 ||
            input.Season is < 1 or > 100 || input.FeedIds is not { Count: > 0 and <= 20 } ||
            input.FeedIds.Distinct().Count() != input.FeedIds.Count || input.WaitMinutes is < 0 or > 43200 ||
            input.Mode is not ("NotifyOnly" or "ManualConfirm" or "AutoDownload") ||
            input.MinimumUpgradeScore is < 1 or > 1000 || input.UpgradeRollbackHours is < 1 or > 720 ||
            input.MinSizeBytes is < 0 || input.MaxSizeBytes is < 0 || input.MinSizeBytes > input.MaxSizeBytes ||
            !ValidList(input.SubtitleGroups) || !ValidList(input.Resolutions) || !ValidList(input.Codecs) ||
            !ValidList(input.Languages) || !ValidList(input.ExcludedKeywords)) return BadRequest();
        try { return Ok(await repository.SaveAsync(input with { Id = id, Name = input.Name.Trim(), TmdbId = tmdb.ToString(System.Globalization.CultureInfo.InvariantCulture) }, cancellationToken)); }
        catch (ArgumentException error) { return Conflict(new { message = error.Message }); }
    }
    [HttpDelete("{id:guid}"), Authorize(Policy = AccessPolicies.ContentWrite)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        await repository.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    [HttpPost("{id:guid}/evaluate"), Authorize(Policy = AccessPolicies.ContentWrite)]
    public async Task<IActionResult> EvaluateAsync(Guid id, CancellationToken cancellationToken)
    {
        var subscription = (await repository.GetAllAsync(cancellationToken)).FirstOrDefault(x => x.Id == id);
        if (subscription == null) return NotFound();
        await coordinator.EvaluateAsync(subscription, cancellationToken);
        return Ok(await repository.GetDecisionsAsync(id, cancellationToken));
    }
    [HttpPost("{id:guid}/episodes/{episode:int}/confirm"), Authorize(Policy = AccessPolicies.ContentWrite)]
    public async Task<IActionResult> ConfirmAsync(Guid id, int episode, CancellationToken cancellationToken)
    {
        var subscription = (await repository.GetAllAsync(cancellationToken)).FirstOrDefault(x => x.Id == id);
        if (subscription == null) return NotFound();
        var result = await coordinator.ConfirmAsync(subscription, episode, cancellationToken);
        return result == null ? Conflict() : Ok(result);
    }
    private static bool ValidList(IReadOnlyList<string>? values) => values is { Count: <= 50 } && values.All(x => x is { Length: > 0 and <= 200 } && !string.IsNullOrWhiteSpace(x));
}
