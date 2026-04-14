using System.Text.Json.Serialization;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Utils;

namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record FileDownloadStatus(
    Guid ItemId,
    double Progress,
    [property: JsonConverter(typeof(JsonNumberTimeSpanConverter))]
    TimeSpan Remaining,
    int Speed,
    [property: JsonConverter(typeof(JsonStringEnumConverter<FileDownloadState>))]
    FileDownloadState State);
