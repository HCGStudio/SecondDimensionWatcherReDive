using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.AI.External;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(OpenAIChatRequest))]
[JsonSerializable(typeof(OpenAIChatChunk))]
[JsonSerializable(typeof(OpenAIModelsResponse))]
[JsonSerializable(typeof(OpenAIResponsesRequest))]
[JsonSerializable(typeof(OpenAIResponsesInputItem))]
[JsonSerializable(typeof(OpenAIResponsesStreamEvent))]
internal partial class OpenAIJsonContext : JsonSerializerContext;
