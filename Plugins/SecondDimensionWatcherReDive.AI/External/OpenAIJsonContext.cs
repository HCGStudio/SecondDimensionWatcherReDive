using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.AI.External;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(OpenAIChatRequest))]
[JsonSerializable(typeof(OpenAIChatChunk))]
[JsonSerializable(typeof(OpenAIModelsResponse))]
internal partial class OpenAIJsonContext : JsonSerializerContext;
