using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.PluginPlatform;

internal sealed record PluginWorkerInvocation(
    string Script,
    string Handler,
    JsonElement Input,
    JsonElement Configuration,
    int MaximumHeapMegabytes,
    int MaximumResponseBytes);

internal sealed record PluginWorkerMessage
{
    public required string Type { get; init; }
    public string? Id { get; init; }
    public string? Capability { get; init; }
    public JsonElement? Payload { get; init; }
    public JsonElement? Result { get; init; }
    public string? Error { get; init; }
}

internal sealed record PluginWorkerBridgeResponse
{
    public required bool Ok { get; init; }
    public JsonElement? Result { get; init; }
    public string? Error { get; init; }
}

[JsonSerializable(typeof(PluginWorkerInvocation))]
[JsonSerializable(typeof(PluginWorkerMessage))]
[JsonSerializable(typeof(PluginWorkerBridgeResponse))]
internal partial class PluginWorkerJsonContext : JsonSerializerContext;
