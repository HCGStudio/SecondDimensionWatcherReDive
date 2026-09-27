using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.Configuration;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(RuntimeSettingsOverrides))]
[JsonSerializable(typeof(RuntimeSecretOverrides))]
internal partial class RuntimeSettingsJsonSerializerContext : JsonSerializerContext;
