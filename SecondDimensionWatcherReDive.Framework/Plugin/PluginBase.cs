using SecondDimensionWatcherReDive.Framework.PluginEvents;
using SecondDimensionWatcherReDive.Framework.PluginParams;

namespace SecondDimensionWatcherReDive.Framework.Plugin;

public abstract class PluginBase : IPlugin
{
    public abstract IPluginInfo Info { get; }
    public int Order { get; protected set; }
    
    protected virtual Task BeforeDownloadBegin(FileDownloadStartParam param)
    {
        return Task.CompletedTask;
    }

    protected virtual Task OnDownloadCompleted(FileDownloadCompleteParam param)
    {
        return Task.CompletedTask;
    }
    
    public virtual void OnLoaded(IPluginServices services)
    {
        services.RegisterEvent<FileDownloadStartParam>(PluginEventName.BeforeDownloadStarted, BeforeDownloadBegin);
        services.RegisterEvent<FileDownloadCompleteParam>(PluginEventName.OnFileDownloadCompleted, OnDownloadCompleted);
    }
}

