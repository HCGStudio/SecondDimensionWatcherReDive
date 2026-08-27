using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Feed;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/subscription-policies")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed class SubscriptionPoliciesController(
    ISubscriptionAutomationPolicyRepository policyRepository,
    IFeedRepository feedRepository,
    ISubscriptionAutomationSimulationService simulationService) : ControllerBase
{
    private const int MaxValuesPerFilter = 32;
    private const int MaxExcludedKeywords = 64;
    private const int MaxFilterValueLength = 128;
    private const int MaxExcludedKeywordLength = 256;

    [HttpGet]
    public async Task<IActionResult> ListPolicies(CancellationToken cancellationToken)
    {
        var policies = await policyRepository.GetAllOrderedAsync(cancellationToken);
        return Ok(policies.Select(policy => policy.ToExternal()).ToList());
    }

    [HttpGet("{feedId:guid}")]
    public async Task<IActionResult> GetPolicy(
        [FromRoute] Guid feedId,
        CancellationToken cancellationToken)
    {
        var policy = await policyRepository.FindByFeedIdAsync(feedId, cancellationToken);
        return policy is null ? NotFound() : Ok(policy.ToExternal());
    }

    [HttpPut("{feedId:guid}")]
    public async Task<IActionResult> UpsertPolicy(
        [FromRoute] Guid feedId,
        [FromBody] External.UpsertSubscriptionAutomationPolicyRequest request,
        CancellationToken cancellationToken)
    {
        if (await feedRepository.FindByIdAsync(feedId, cancellationToken) is null)
            return NotFound(new { error = "Feed does not exist." });

        var now = DateTimeOffset.UtcNow;
        if (!TryCreatePolicy(feedId, request, now, out var policy, out var error))
            return BadRequest(new { error });

        var existing = await policyRepository.FindByFeedIdAsync(feedId, cancellationToken);
        policy = policy with { CreatedAt = existing?.CreatedAt ?? now };

        var saved = await policyRepository.UpsertAsync(policy, cancellationToken);
        return Ok(saved.ToExternal());
    }

    [HttpPost("{feedId:guid}/simulate")]
    public async Task<IActionResult> SimulatePolicy(
        [FromRoute] Guid feedId,
        [FromBody] External.UpsertSubscriptionAutomationPolicyRequest request,
        CancellationToken cancellationToken)
    {
        if (await feedRepository.FindByIdAsync(feedId, cancellationToken) is null)
            return NotFound(new { error = "Feed does not exist." });

        var now = DateTimeOffset.UtcNow;
        if (!TryCreatePolicy(feedId, request, now, out var policy, out var error))
            return BadRequest(new { error });

        var result = await simulationService.SimulateAsync(policy, cancellationToken);
        return Ok(result.ToExternal());
    }

    [HttpDelete("{feedId:guid}")]
    public async Task<IActionResult> DeletePolicy(
        [FromRoute] Guid feedId,
        CancellationToken cancellationToken)
    {
        var deleted = await policyRepository.DeleteByFeedIdAsync(feedId, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    private static bool TryCreatePolicy(
        Guid feedId,
        External.UpsertSubscriptionAutomationPolicyRequest request,
        DateTimeOffset timestamp,
        out SubscriptionAutomationPolicy policy,
        out string? error)
    {
        policy = null!;
        if (!Enum.TryParse<SubscriptionAutomationMode>(request.Mode, ignoreCase: false, out var mode)
            || !Enum.IsDefined(mode))
        {
            error = "Mode must be one of NotifyOnly, ManualConfirm or AutoDownload.";
            return false;
        }

        if (!TryNormalizeValues(request.SubtitleGroups, MaxValuesPerFilter, MaxFilterValueLength,
                "subtitleGroups", out var subtitleGroups, out error)
            || !TryNormalizeValues(request.Resolutions, MaxValuesPerFilter, MaxFilterValueLength,
                "resolutions", out var resolutions, out error)
            || !TryNormalizeValues(request.Codecs, MaxValuesPerFilter, MaxFilterValueLength,
                "codecs", out var codecs, out error)
            || !TryNormalizeValues(request.Languages, MaxValuesPerFilter, MaxFilterValueLength,
                "languages", out var languages, out error)
            || !TryNormalizeValues(request.ExcludedKeywords, MaxExcludedKeywords, MaxExcludedKeywordLength,
                "excludedKeywords", out var excludedKeywords, out error))
            return false;

        if (request.MinSizeBytes is < 0)
        {
            error = "minSizeBytes cannot be negative.";
            return false;
        }

        if (request.MaxSizeBytes is < 0)
        {
            error = "maxSizeBytes cannot be negative.";
            return false;
        }

        if (request.MinSizeBytes.HasValue
            && request.MaxSizeBytes.HasValue
            && request.MinSizeBytes > request.MaxSizeBytes)
        {
            error = "minSizeBytes cannot be greater than maxSizeBytes.";
            return false;
        }

        policy = new SubscriptionAutomationPolicy(
            feedId,
            subtitleGroups,
            resolutions,
            codecs,
            languages,
            request.MinSizeBytes,
            request.MaxSizeBytes,
            excludedKeywords,
            mode,
            timestamp,
            timestamp);
        error = null;
        return true;
    }

    private static bool TryNormalizeValues(
        IReadOnlyList<string>? values,
        int maxCount,
        int maxLength,
        string fieldName,
        out IReadOnlyList<string> normalized,
        out string? error)
    {
        values ??= [];
        if (values.Count > maxCount)
        {
            normalized = [];
            error = $"{fieldName} cannot contain more than {maxCount} values.";
            return false;
        }

        var result = new List<string>(values.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                normalized = [];
                error = $"{fieldName} cannot contain empty values.";
                return false;
            }

            var trimmed = value.Trim();
            if (trimmed.Length > maxLength)
            {
                normalized = [];
                error = $"Each {fieldName} value must be at most {maxLength} characters.";
                return false;
            }

            if (seen.Add(trimmed)) result.Add(trimmed);
        }

        normalized = result;
        error = null;
        return true;
    }
}
