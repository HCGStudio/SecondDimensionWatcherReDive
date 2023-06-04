namespace SecondDimensionWatcherReDive.Models;

public class AnimationGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class AnimationGroupDto
{
    public string Name { get; set; } = string.Empty;
}