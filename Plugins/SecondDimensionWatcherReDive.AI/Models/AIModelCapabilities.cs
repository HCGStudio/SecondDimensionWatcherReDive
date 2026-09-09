using SecondDimensionWatcherReDive.AI.Configuration;

namespace SecondDimensionWatcherReDive.AI.Models;

/// <summary>
///     Known API capabilities, supplemented by per-endpoint model configuration for aliases and
///     compatible services. Unknown models remain selectable without guessing their effort support.
/// </summary>
public static class AIModelCapabilities
{
    public static IReadOnlyList<string> GetReasoningEfforts(AIProviderProtocol protocol, string model)
    {
        if (protocol == AIProviderProtocol.Anthropic)
        {
            if (IsModel(model, "claude-opus-4-5")) return ["low", "medium", "high"];
            if (IsModel(model, "claude-opus-4-6") || IsModel(model, "claude-sonnet-4-6") ||
                IsModel(model, "claude-mythos-preview")) return ["low", "medium", "high", "max"];
            if (IsModel(model, "claude-opus-4-7") || IsModel(model, "claude-opus-4-8") ||
                IsModel(model, "claude-opus-5") || IsModel(model, "claude-sonnet-5") ||
                IsModel(model, "claude-fable-5") || IsModel(model, "claude-mythos-5"))
                return ["low", "medium", "high", "xhigh", "max"];
            return [];
        }

        if (IsModel(model, "gpt-6-astra")) return ["low", "medium", "high", "xhigh", "max"];
        if (IsModel(model, "gpt-5.6")) return ["none", "low", "medium", "high", "xhigh", "max"];
        if (model.Contains("-chat", StringComparison.Ordinal) || model.Contains("-pro", StringComparison.Ordinal))
            return [];
        if (IsModel(model, "gpt-5.2") || IsModel(model, "gpt-5.3-codex") ||
            IsModel(model, "gpt-5.4") || IsModel(model, "gpt-5.5"))
            return model.Contains("codex", StringComparison.Ordinal)
                ? ["low", "medium", "high", "xhigh"] : ["none", "low", "medium", "high", "xhigh"];
        if (IsModel(model, "gpt-5.1"))
            return model.Contains("codex", StringComparison.Ordinal)
                ? ["low", "medium", "high"] : ["none", "low", "medium", "high"];
        if (IsModel(model, "gpt-5")) return ["minimal", "low", "medium", "high"];
        if (IsModel(model, "o1") || IsModel(model, "o3") || IsModel(model, "o4-mini") ||
            IsModel(model, "gpt-oss-20b") || IsModel(model, "gpt-oss-120b"))
            return ["low", "medium", "high"];
        return [];
    }

    public static string? ValidateEffort(string model, string? effort, IReadOnlyList<string> supported)
    {
        if (string.IsNullOrWhiteSpace(effort)) return null;
        var normalized = effort.Trim().ToLowerInvariant();
        if (!supported.Contains(normalized, StringComparer.Ordinal))
            throw new ArgumentException($"Model '{model}' does not support reasoning effort '{effort}'.");
        return normalized;
    }

    public static bool IsModel(string model, string family)
        => model.Equals(family, StringComparison.Ordinal) || model.StartsWith(family + "-", StringComparison.Ordinal);

    public static bool IsOfficialOpenAIEndpoint(string baseUrl)
        => Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint) &&
           (endpoint.Host.Equals("api.openai.com", StringComparison.OrdinalIgnoreCase) ||
            endpoint.Host.EndsWith(".api.openai.com", StringComparison.OrdinalIgnoreCase));

    public static void ValidateToolProtocol(
        AIProviderProtocol protocol, string baseUrl, string model, string? effort, bool requiresTools)
    {
        if (!requiresTools || protocol != AIProviderProtocol.OpenAIChatCompletions ||
            !IsOfficialOpenAIEndpoint(baseUrl)) return;
        if (IsModel(model, "gpt-6-astra"))
            throw new ArgumentException("GPT-6 Astra tool calling requires the OpenAI Responses protocol.");
        if (IsModel(model, "gpt-5.6") && !string.Equals(effort, "none", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("GPT-5.6 tool calling with reasoning requires the OpenAI Responses protocol; Chat Completions requires effort none.");
    }
}
