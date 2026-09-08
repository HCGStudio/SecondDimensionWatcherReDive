using System.Text.Json.Nodes;
using SecondDimensionWatcherReDive.Framework.ConfigurationMigration;

namespace SecondDimensionWatcherReDive.ConfigMigration.Migrations;

internal sealed class MoveInferenceToAi : IConfigMigration
{
    internal static readonly string[] LegacyKeys = ["Provider", "ApiKey", "BaseUrl", "Model", "MaxTokens"];
    public ConfigMigrationDefinition Definition { get; } = new(
        new Version(2, 2, 0), new Version(2, 2, 1),
        "Move legacy Inference provider settings to AI.", MayRequireUserIntervention: true);

    public IReadOnlyList<ConfigMigrationChoice> GetRequiredChoices(ConfigMigrationContext context)
    {
        var config = context.Configuration;
        if (!HasLegacy(config)) return [];
        var provider = Provider(context);
        var mappings = Mappings(provider);
        return mappings.Where(pair => ConfigTree.Get(config, pair.Source) is { } source
                && ConfigTree.Get(config, pair.Target) is { } target && !Equivalent(source, target))
            .Select(pair => new ConfigMigrationChoice(pair.Target,
                $"Both '{pair.Source}' and '{pair.Target}' are set differently. Choose which setting to retain.",
                [new("current", "Keep the AI setting"), new("legacy", "Use the old Inference setting")]))
            .ToArray();
    }

    public void Up(ConfigMigrationContext context, IReadOnlyDictionary<string, string> selections)
    {
        var config = context.Configuration;
        if (!HasLegacy(config)) return;
        var provider = Provider(context);
        foreach (var pair in Mappings(provider))
        {
            var source = ConfigTree.Get(config, pair.Source);
            if (source is null) continue;
            if (ConfigTree.Get(config, pair.Target) is null
                || selections.GetValueOrDefault(pair.Target) == "legacy")
                ConfigTree.Set(config, pair.Target, source);
        }
        if (ConfigTree.Get(config, "AI:Provider") is null
            && context.InheritedSettings?.GetValueOrDefault("AI:Provider") is null)
            ConfigTree.Set(config, "AI:Provider", JsonValue.Create(provider));
        foreach (var key in LegacyKeys) ConfigTree.Remove(config, $"Inference:{key}");
    }

    private static bool HasLegacy(JsonObject config) => LegacyKeys.Any(key => ConfigTree.Get(config, $"Inference:{key}") is not null);

    private static string Provider(ConfigMigrationContext context)
    {
        var provider = ConfigTree.Text(context.Configuration, "Inference:Provider")
                       ?? ConfigTree.Text(context.Configuration, "AI:Provider")
                       ?? context.InheritedSettings?.GetValueOrDefault("AI:Provider")
                       ?? "OpenAI";
        if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)) return "OpenAI";
        if (provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase)) return "Anthropic";
        throw new ConfigMigrationException("Inference:Provider must be OpenAI or Anthropic before migration.");
    }

    private static IEnumerable<(string Source, string Target)> Mappings(string provider)
    {
        yield return ("Inference:Provider", "AI:Provider");
        foreach (var key in LegacyKeys.Skip(1)) yield return ($"Inference:{key}", $"AI:{provider}:{key}");
    }

    private static bool Equivalent(JsonNode left, JsonNode right) => JsonNode.DeepEquals(left, right)
        || (left is JsonValue && right is JsonValue && left.ToString() == right.ToString());
}
