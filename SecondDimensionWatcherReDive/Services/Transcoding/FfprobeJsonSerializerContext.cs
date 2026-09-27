using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.Services.Transcoding;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(FfmpegProcessRunner.FfprobeDocument))]
internal partial class FfprobeJsonSerializerContext : JsonSerializerContext;
