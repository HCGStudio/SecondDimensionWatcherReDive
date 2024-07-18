namespace SecondDimensionWatcherReDive.Framework.Plugin;

public interface IPluginInfo
{
    string Name { get; }
    string Description { get; }
    string License { get; }
    string SupportLink { get; }
}