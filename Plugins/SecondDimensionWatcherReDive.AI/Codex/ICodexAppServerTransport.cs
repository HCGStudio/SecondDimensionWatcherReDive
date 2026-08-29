namespace SecondDimensionWatcherReDive.AI.Codex;

public interface ICodexAppServerTransportFactory
{
    Task<ICodexAppServerTransport> ConnectAsync(
        Uri endpoint,
        string? bearerToken,
        CancellationToken cancellationToken);
}

public interface ICodexAppServerTransport : IAsyncDisposable
{
    ValueTask SendAsync(string message, CancellationToken cancellationToken);

    ValueTask<string?> ReceiveAsync(CancellationToken cancellationToken);
}
