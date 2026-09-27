using System.Text.Json;
using System.Text.Json.Serialization;
using SecondDimensionWatcherReDive.Framework.Inference;

namespace SecondDimensionWatcherReDive.Inference.AI.Tools;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(GetTmdbSeasonEpisodesParams))]
[JsonSerializable(typeof(GetTmdbSeasonsParams))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(SaveFileNameRegexRuleParams))]
[JsonSerializable(typeof(SaveFileNameRegexRuleResult))]
[JsonSerializable(typeof(SearchTmdbParams))]
[JsonSerializable(typeof(IReadOnlyList<FileNameInferenceInput>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(TmdbTvSearchResult[]))]
[JsonSerializable(typeof(TmdbMovieSearchResult[]))]
[JsonSerializable(typeof(TmdbSeasonsResult))]
[JsonSerializable(typeof(TmdbSeasonEpisodesResult))]
internal partial class InferenceToolJsonContext : JsonSerializerContext;
