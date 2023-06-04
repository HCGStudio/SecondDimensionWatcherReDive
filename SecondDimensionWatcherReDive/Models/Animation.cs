namespace SecondDimensionWatcherReDive.Models;

public class Animation
{
    public Guid Id { get; set; }

    public string TmdbId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string OriginalName { get; set; } = string.Empty;
}

public class AnimationDto
{
    public string Name { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;

    public string TmdbId { get; set; } = string.Empty;
}