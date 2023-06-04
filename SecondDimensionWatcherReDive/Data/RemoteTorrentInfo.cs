using System.Text.Json.Serialization;
using SecondDimensionWatcherReDive.Utils;

namespace SecondDimensionWatcherReDive.Data;

public class RemoteTorrentInfo
{
    [JsonPropertyName("eta")]
    [JsonConverter(typeof(JsonNumberTimeSpanConverter))]
    public TimeSpan Eta { get; set; }

    [JsonPropertyName("state")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RemoteTorrentState State { get; set; }

    [JsonPropertyName("progress")] public double Progress { get; set; }

    [JsonPropertyName("content_path")] public string SavePath { get; set; } = string.Empty;

    [JsonPropertyName("dlspeed")] public int Speed { get; set; }

    [JsonPropertyName("hash")] public string Hash { get; set; } = string.Empty;
}