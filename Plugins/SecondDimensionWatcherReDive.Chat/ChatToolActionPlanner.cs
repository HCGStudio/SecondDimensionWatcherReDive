using System.Text.Json;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Chat;

internal sealed record ChatToolActionPlan(
    ToolRiskLevel RiskLevel,
    string ParameterSummary,
    string ImpactSummary,
    bool IsReversible);

internal interface IChatToolActionPlanner
{
    Task<ChatToolActionPlan> PlanAsync(
        ToolDefinition definition,
        ToolCall toolCall,
        CancellationToken cancellationToken);
}

internal sealed class ChatToolActionPlanner(
    IAnimationInfoRepository animationInfoRepository,
    IFileMappingRepository fileMappingRepository,
    IFeedRepository feedRepository) : IChatToolActionPlanner
{
    public async Task<ChatToolActionPlan> PlanAsync(
        ToolDefinition definition,
        ToolCall toolCall,
        CancellationToken cancellationToken)
    {
        if (definition.RiskLevel == ToolRiskLevel.ReadOnly)
            return ReadOnly(toolCall.Name);

        try
        {
            using var document = JsonDocument.Parse(toolCall.Arguments);
            return toolCall.Name switch
            {
                "manage_feeds" => await PlanFeedsAsync(document.RootElement, cancellationToken),
                "manage_tasks" => PlanTasks(document.RootElement),
                "manage_downloads" => await PlanDownloadsAsync(document.RootElement, cancellationToken),
                "subscribe_bangumi" => PlanSubscription(document.RootElement),
                _ => DefaultPlan(definition, toolCall.Name)
            };
        }
        catch (JsonException)
        {
            // Malformed arguments fail closed at the tool's declared maximum risk. If the user
            // approves, normal tool deserialization will still reject the payload without mutation.
            return DefaultPlan(definition, toolCall.Name);
        }
    }

    private async Task<ChatToolActionPlan> PlanFeedsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        return GetAction(arguments) switch
        {
            "list" => ReadOnly("manage_feeds.list"),
            "add" => new(
                ToolRiskLevel.Mutating,
                $"action=add; target={SanitizeFeedTarget(GetString(arguments, "url"))}",
                $"Add one RSS subscription for {SanitizeFeedTarget(GetString(arguments, "url"))}.",
                true),
            "remove" => await PlanFeedRemovalAsync(arguments, cancellationToken),
            _ => new(
                ToolRiskLevel.Destructive,
                "action=unknown",
                "Run an unrecognized feed-management action.",
                false)
        };
    }

    private async Task<ChatToolActionPlan> PlanFeedRemovalAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var idText = GetString(arguments, "id");
        Feed? feed = null;
        if (Guid.TryParse(idText, out var id))
            feed = await feedRepository.FindByIdAsync(id, cancellationToken);
        var target = feed is null
            ? ShortValue(idText)
            : SanitizeFeedTarget(feed.Url);
        return new(
            ToolRiskLevel.Destructive,
            $"action=remove; feed={target}",
            $"Remove the RSS subscription for {target}.",
            true);
    }

    private static ChatToolActionPlan PlanTasks(JsonElement arguments) =>
        GetAction(arguments) switch
        {
            "list" => ReadOnly("manage_tasks.list"),
            "run" => new(
                ToolRiskLevel.Mutating,
                $"action=run; task={ShortValue(GetString(arguments, "task_id"))}",
                $"Enqueue background task {ShortValue(GetString(arguments, "task_id"))} for execution.",
                false),
            _ => new(
                ToolRiskLevel.Mutating,
                "action=unknown",
                "Run an unrecognized task-management action.",
                false)
        };

    private async Task<ChatToolActionPlan> PlanDownloadsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var action = GetAction(arguments);
        var idText = GetString(arguments, "animation_id");
        AnimationInfo? animation = null;
        IReadOnlyList<FileMapping> mappings = [];
        if (Guid.TryParse(idText, out var animationId))
        {
            animation = await animationInfoRepository.FindByIdAsync(animationId, cancellationToken);
            if (action == "cancel" && GetBoolean(arguments, "remove_file"))
                mappings = await fileMappingRepository.GetForAnimationInfoAsync(
                    animationId, cancellationToken);
        }

        var target = animation is null ? ShortValue(idText) : SafeText(animation.Title);
        return action switch
        {
            "start" => new(
                ToolRiskLevel.Mutating,
                $"action=start; animation={target}",
                $"Start the download for {target}.",
                true),
            "pause" => new(
                ToolRiskLevel.Mutating,
                $"action=pause; animation={target}",
                $"Pause the active download for {target}.",
                true),
            "resume" => new(
                ToolRiskLevel.Mutating,
                $"action=resume; animation={target}",
                $"Resume the download for {target}.",
                true),
            "cancel" when GetBoolean(arguments, "remove_file") => new(
                ToolRiskLevel.Destructive,
                $"action=cancel; remove_file=true; animation={target}; mapped_files={mappings.Count}",
                $"Cancel {target}, delete its downloaded payload, and make {mappings.Count} mapped file(s) unavailable.",
                false),
            "cancel" => new(
                ToolRiskLevel.Destructive,
                $"action=cancel; remove_file=false; animation={target}",
                $"Cancel the download for {target} without requesting payload deletion.",
                false),
            _ => new(
                ToolRiskLevel.Destructive,
                $"action=unknown; animation={target}",
                $"Run an unrecognized download-management action for {target}.",
                false)
        };
    }

    private static ChatToolActionPlan PlanSubscription(JsonElement arguments)
    {
        var mikanId = GetInt32(arguments, "mikan_id")?.ToString() ?? "unknown";
        var subgroupId = GetInt32(arguments, "subgroup_id")?.ToString() ?? "all";
        return new(
            ToolRiskLevel.Mutating,
            $"mikan_id={mikanId}; subgroup_id={subgroupId}",
            $"Create one RSS subscription for Mikan bangumi {mikanId} (subgroup {subgroupId}).",
            true);
    }

    private static ChatToolActionPlan DefaultPlan(ToolDefinition definition, string toolName) => new(
        definition.RiskLevel,
        "parameters=redacted",
        $"Execute AI tool {SafeText(toolName)} with its exact stored parameters.",
        false);

    private static ChatToolActionPlan ReadOnly(string toolName) => new(
        ToolRiskLevel.ReadOnly,
        "read_only=true",
        $"Read data through {SafeText(toolName)}.",
        true);

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetAction(JsonElement element) =>
        GetString(element, "action")?.Trim().ToLowerInvariant();

    private static bool GetBoolean(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean();

    private static int? GetInt32(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static string SanitizeFeedTarget(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return "an unspecified endpoint";
        return SafeText(uri.GetComponents(UriComponents.HostAndPort | UriComponents.Path,
            UriFormat.Unescaped));
    }

    private static string ShortValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : SafeText(value);

    private static string SafeText(string value)
    {
        var sanitized = new string(value
            .Where(character => !char.IsControl(character))
            .Take(160)
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}
