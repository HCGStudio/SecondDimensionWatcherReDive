using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.AI.Models;

public static class ToolJsonOptions
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };
}
