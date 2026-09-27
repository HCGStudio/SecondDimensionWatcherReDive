using System.Text.Json;
using System.Text.Json.Serialization;
using SecondDimensionWatcherReDive.Framework.Feed;

namespace SecondDimensionWatcherReDive.Repositories;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(IReadOnlyList<SubscriptionAutomationExplanation>), TypeInfoPropertyName = "Explanations")]
[JsonSerializable(typeof(IReadOnlyList<string>), TypeInfoPropertyName = "Reasons")]
internal partial class AutomationJsonSerializerContext : JsonSerializerContext;
