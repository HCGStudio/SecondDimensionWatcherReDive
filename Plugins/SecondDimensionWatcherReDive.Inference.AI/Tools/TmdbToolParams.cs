using System.ComponentModel;

namespace SecondDimensionWatcherReDive.Inference.AI.Tools;

internal sealed record SearchTmdbParams(
    [property: Description("The anime name to search for")]
    string Query);

internal sealed record GetTmdbSeasonsParams(
    [property: Description("The TMDB TV show ID")]
    int TmdbId);

internal sealed record GetTmdbSeasonEpisodesParams(
    [property: Description("The TMDB TV show ID")]
    int TmdbId,
    [property: Description("The season number to get episodes for")]
    int SeasonNumber);
