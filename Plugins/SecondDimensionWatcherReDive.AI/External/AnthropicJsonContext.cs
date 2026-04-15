using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.AI.External;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(AnthropicMessagesRequest))]
[JsonSerializable(typeof(AnthropicMessageStartData))]
[JsonSerializable(typeof(AnthropicContentBlockStartData))]
[JsonSerializable(typeof(AnthropicContentBlockDeltaData))]
[JsonSerializable(typeof(AnthropicContentBlockStopData))]
[JsonSerializable(typeof(AnthropicMessageDeltaData))]
[JsonSerializable(typeof(AnthropicModelsResponse))]
internal partial class AnthropicJsonContext : JsonSerializerContext;
