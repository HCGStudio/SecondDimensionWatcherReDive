using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.IntegrationTest.Helpers;
using SecondDimensionWatcherReDive.NFS;
using SecondDimensionWatcherReDive.NFS.Server;

namespace SecondDimensionWatcherReDive.IntegrationTest.Nfs;

internal sealed class NfsTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _serverTask;

    public List<FileMapping> Mappings { get; }
    public Mock<IFileStore> FileStoreMock { get; }
    public Mock<IFileStoreProvider> FileStoreProviderMock { get; }
    public int Port { get; }

    private NfsTestHost(
        ServiceProvider provider,
        List<FileMapping> mappings,
        Mock<IFileStore> fileStore,
        Mock<IFileStoreProvider> fileStoreProvider,
        int port,
        CancellationTokenSource cts,
        Task serverTask)
    {
        _provider = provider;
        Mappings = mappings;
        FileStoreMock = fileStore;
        FileStoreProviderMock = fileStoreProvider;
        Port = port;
        _cts = cts;
        _serverTask = serverTask;
    }

    public static NfsTestHost Start()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Nfs:Enabled"] = "true",
                ["Nfs:Port"] = "0",
                ["Nfs:BindAddress"] = "127.0.0.1",
                ["Nfs:LeaseSeconds"] = "90",
                ["Nfs:MaxConnections"] = "32",
            })
            .Build();

        var mappings = new List<FileMapping>();
        var fileStore = new Mock<IFileStore>();
        var fileStoreProvider = new Mock<IFileStoreProvider>();
        fileStoreProvider.Setup(p => p.GetClient("local")).Returns(fileStore.Object);
        var mappingRepo = new FakeFileMappingRepository(mappings);
        var explorer = new FakeFileExplorer(mappings, fileStore.Object, mappingRepo);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IFileMappingRepository>(_ => mappingRepo);
        services.AddSingleton<IFileExplorer>(_ => explorer);
        services.AddSingleton<IFileStoreProvider>(_ => fileStoreProvider.Object);
        services.AddSingleton<IFileStore>(_ => fileStore.Object);
        services.AddNfs();

        var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<NfsTcpServer>();
        server.Bind();
        var port = server.BoundPort;

        var cts = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.RunAsync(cts.Token));

        return new NfsTestHost(provider, mappings, fileStore, fileStoreProvider, port, cts, serverTask);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try
        {
            await _serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
        }
        await _provider.DisposeAsync();
    }
}
