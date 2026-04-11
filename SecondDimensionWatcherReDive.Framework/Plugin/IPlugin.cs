namespace SecondDimensionWatcherReDive.Framework.Plugin;

public interface IPlugin
{
    IPluginInfo Info { get; }
    int Order { get; }
    void OnLoaded(IPluginServices services);
}
