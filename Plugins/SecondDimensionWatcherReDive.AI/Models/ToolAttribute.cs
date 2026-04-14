namespace SecondDimensionWatcherReDive.AI.Models;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ToolAttribute<TParam>(string name, string description) : Attribute
{
    public string Name { get; } = name;
    public string Description { get; } = description;
}
