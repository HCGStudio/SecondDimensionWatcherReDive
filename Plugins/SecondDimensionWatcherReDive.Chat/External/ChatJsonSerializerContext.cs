using System.Text.Json.Serialization;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Chat.External;

[JsonSerializable(typeof(ChatStatusResponse))]
[JsonSerializable(typeof(SendMessageRequest))]
[JsonSerializable(typeof(CreateConversationRequest))]
[JsonSerializable(typeof(UpdateConversationRequest))]
[JsonSerializable(typeof(ChatConversationSummary))]
[JsonSerializable(typeof(IReadOnlyList<ChatConversationSummary>))]
[JsonSerializable(typeof(ChatConversationDetail))]
[JsonSerializable(typeof(IReadOnlyList<AIModel>))]
[JsonSerializable(typeof(AIModel[]))]
// SSE event types (serialized via JsonSerializer.Serialize with typed JsonTypeInfo)
[JsonSerializable(typeof(SseTextDelta))]
[JsonSerializable(typeof(SseToolCallBegin))]
[JsonSerializable(typeof(SseToolCallDelta))]
[JsonSerializable(typeof(SseToolResult))]
[JsonSerializable(typeof(SseFinished))]
[JsonSerializable(typeof(SseError))]
// Persisted tool call (serialized into ToolCallsJson column)
[JsonSerializable(typeof(IEnumerable<ToolCallRecord>))]
internal partial class ChatJsonSerializerContext : JsonSerializerContext;
