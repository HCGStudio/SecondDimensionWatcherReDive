using System.Text.Json.Serialization;
using SecondDimensionWatcherReDive.Data;

namespace SecondDimensionWatcherReDive;

[JsonSerializable(typeof(RemoteTorrentInfo[]))]
public partial class QBittorrentJsonSerializerContext : JsonSerializerContext;
