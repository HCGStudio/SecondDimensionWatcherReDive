using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.Auth;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(RefreshTokenState))]
[JsonSerializable(typeof(RefreshTokenReplacement))]
internal partial class RefreshTokenJsonSerializerContext : JsonSerializerContext;
