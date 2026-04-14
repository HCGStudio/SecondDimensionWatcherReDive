using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.AI.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(OpenAiChatRequest))]
[JsonSerializable(typeof(OpenAiChatChunk))]
[JsonSerializable(typeof(OpenAiModelsResponse))]
internal partial class OpenAiJsonContext : JsonSerializerContext;
