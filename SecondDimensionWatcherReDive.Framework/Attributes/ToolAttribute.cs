using SecondDimensionWatcherReDive.Framework.AI;

namespace SecondDimensionWatcherReDive.Framework.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ToolAttribute<TParam>(
    string name,
    string description,
    ToolRiskLevel riskLevel,
    Type jsonSerializerContextType) : Attribute
{
    public Type JsonSerializerContextType { get; } = jsonSerializerContextType;
    public string Name { get; } = name;
    public string Description { get; } = description;
    public ToolRiskLevel RiskLevel { get; } = riskLevel;
}
