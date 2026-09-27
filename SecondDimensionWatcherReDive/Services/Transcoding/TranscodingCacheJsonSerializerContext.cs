using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.Services.Transcoding;

[JsonSerializable(typeof(HlsTranscodingService.CacheManifest))]
internal partial class TranscodingCacheJsonSerializerContext : JsonSerializerContext;
