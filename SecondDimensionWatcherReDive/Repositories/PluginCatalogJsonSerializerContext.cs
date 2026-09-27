using System.Text.Json;
using System.Text.Json.Serialization;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(PluginCatalogEntry))]
[JsonSerializable(typeof(RetainedPluginData))]
internal partial class PluginCatalogJsonSerializerContext : JsonSerializerContext;
