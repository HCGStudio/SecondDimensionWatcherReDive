using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.Services;

public sealed class ScanMediaLibraries(
    IServiceScopeFactory scopeFactory,
    IMediaLibraryScanQueue scanQueue,
    IOptionsMonitor<MediaLibraryOptions> options) : ScheduledTaskBase
{
    public override string Id => "ScanMediaLibraries";

    public override TimeSpan Interval
    {
        get
        {
            var configured = options.CurrentValue.ScanInterval;
            return configured > TimeSpan.Zero ? configured : TimeSpan.FromMinutes(5);
        }
    }

    protected override async Task ExecuteTaskAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMediaLibrarySourceRepository>();
        var sources = await repository.GetAllAsync(cancellationToken);
        foreach (var source in sources.Where(source => source.IsMonitoring))
            scanQueue.Enqueue(source.Id);
    }
}
