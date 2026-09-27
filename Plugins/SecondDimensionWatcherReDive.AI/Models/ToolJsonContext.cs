using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.AI.Models;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ToolSuccessPayload))]
[JsonSerializable(typeof(ToolFailurePayload))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonNode))]
internal partial class ToolJsonContext : JsonSerializerContext;

public sealed class ToolEnumConverter<T>() : JsonStringEnumConverter<T>(JsonNamingPolicy.SnakeCaseLower)
    where T : struct, Enum;
