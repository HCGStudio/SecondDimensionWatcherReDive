namespace SecondDimensionWatcherReDive.Framework.Animation;

public class AnimationGroupedResponse
{
    public List<AnimationWithEpisodes> Animations { get; set; } = [];
    public List<AnimationInfoDto> Uncategorized { get; set; } = [];
}

public class AnimationWithEpisodes
{
    public string TmdbId { get; set; } = "";
    public string Name { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public string? PosterPath { get; set; }
    public int EpisodeCount { get; set; }
    public List<AnimationInfoDto> Episodes { get; set; } = [];
}
