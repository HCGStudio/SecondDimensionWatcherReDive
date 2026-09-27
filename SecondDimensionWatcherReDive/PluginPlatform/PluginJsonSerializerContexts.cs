using System.Text.Json;
using System.Text.Json.Serialization;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.Plugin;

namespace SecondDimensionWatcherReDive.PluginPlatform;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(NetworkCapabilityRequest))]
[JsonSerializable(typeof(PluginNetworkResponse))]
[JsonSerializable(typeof(PluginBinaryResponse))]
[JsonSerializable(typeof(PluginFileListEntry[]))]
[JsonSerializable(typeof(PluginFileEntry[]))]
[JsonSerializable(typeof(PluginWriteResponse))]
[JsonSerializable(typeof(PluginExistsResponse))]
[JsonSerializable(typeof(PluginPathRequest))]
[JsonSerializable(typeof(FileStoreInfo))]
[JsonSerializable(typeof(FileStoreInfo[]))]
[JsonSerializable(typeof(PluginManifest))]
internal partial class PluginWebJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(PluginLifecycleJournal))]
internal partial class PluginJournalJsonContext : JsonSerializerContext;
