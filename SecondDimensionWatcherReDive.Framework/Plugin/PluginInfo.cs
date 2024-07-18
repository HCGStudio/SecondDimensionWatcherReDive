namespace SecondDimensionWatcherReDive.Framework.Plugin;

public record PluginInfo(string Name, string Description, string License, string SupportLink) : IPluginInfo;