using SecondDimensionWatcherReDive.Framework.Plugin;
using SecondDimensionWatcherReDive.Framework.PluginEvents;
using SecondDimensionWatcherReDive.Framework.PluginParams;

namespace SecondDimensionWatcherReDive.Plugin;

public static class PluginHelper
{
    public static WebApplicationBuilder InitializePlugin(this WebApplicationBuilder webApplicationBuilder)
    {
        var beforeDownloadStarted = new PluginEvent<FileDownloadStartParam>();
        var onFileDownloadCompleted = new PluginEvent<FileDownloadCompleteParam>();

        webApplicationBuilder.Services.AddSingleton<IPluginEventTrigger<FileDownloadStartParam>>(beforeDownloadStarted);
        webApplicationBuilder.Services.AddSingleton<IPluginEventTrigger<FileDownloadCompleteParam>>(onFileDownloadCompleted);

        webApplicationBuilder.Services.AddSingleton<IPluginServices>(sp =>
        {
            var services = new PluginServices(sp);
            services.AddEvent(PluginEventName.BeforeDownloadStarted, beforeDownloadStarted);
            services.AddEvent(PluginEventName.OnFileDownloadCompleted, onFileDownloadCompleted);
            return services;
        });

        return webApplicationBuilder;
    }
}
