using System.Text.Json.Serialization;
using SecondDimensionWatcherReDive.Utils;

namespace SecondDimensionWatcherReDive.Data;

public record struct FileDownloadStatus(
    Guid ItemId,
    double Progress,
    [property: JsonConverter(typeof(JsonNumberTimeSpanConverter))]
    TimeSpan Remaining,
    int Speed,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    FileDownloadState State);