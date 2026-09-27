using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.ConfigMigration;

[JsonSerializable(typeof(JsonNode))]
internal partial class ConfigJsonSerializerContext : JsonSerializerContext
{
    internal static ConfigJsonSerializerContext Configuration { get; } = new(new JsonSerializerOptions
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    });
}
