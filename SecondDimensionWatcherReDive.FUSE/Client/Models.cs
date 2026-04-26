using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.FUSE.Client;

internal sealed record VfsEntry(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("isDirectory")] bool IsDirectory,
    [property: JsonPropertyName("size")] long? Size,
    [property: JsonPropertyName("lastModifiedUtc")] DateTimeOffset? LastModifiedUtc);

[JsonSerializable(typeof(VfsEntry))]
[JsonSerializable(typeof(VfsEntry[]))]
internal partial class SdwJsonContext : JsonSerializerContext;
