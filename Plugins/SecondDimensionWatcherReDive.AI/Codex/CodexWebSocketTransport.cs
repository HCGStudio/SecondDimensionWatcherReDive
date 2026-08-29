using System.Buffers;
using System.Net.WebSockets;
using System.Text;

namespace SecondDimensionWatcherReDive.AI.Codex;

internal sealed class CodexWebSocketTransportFactory : ICodexAppServerTransportFactory
{
    public async Task<ICodexAppServerTransport> ConnectAsync(
        Uri endpoint,
        string? bearerToken,
        CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        if (!string.IsNullOrWhiteSpace(bearerToken))
            socket.Options.SetRequestHeader("Authorization", $"Bearer {bearerToken}");

        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken);
            return new CodexWebSocketTransport(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

internal sealed class CodexWebSocketTransport(ClientWebSocket socket) : ICodexAppServerTransport
{
    private const int ReceiveBufferSize = 8192;
    private const int MaximumMessageBytes = 16 * 1024 * 1024;

    public async ValueTask SendAsync(string message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    public async ValueTask<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        try
        {
            var writer = new ArrayBufferWriter<byte>();
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;
                if (result.MessageType != WebSocketMessageType.Text)
                    throw new InvalidDataException("Codex app-server sent a non-text WebSocket message.");

                if (writer.WrittenCount + result.Count > MaximumMessageBytes)
                    throw new InvalidDataException("Codex app-server message exceeded the 16 MiB limit.");

                buffer.AsSpan(0, result.Count).CopyTo(writer.GetSpan(result.Count));
                writer.Advance(result.Count);
                if (result.EndOfMessage)
                    return Encoding.UTF8.GetString(writer.WrittenSpan);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await socket.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "client disconnect",
                    timeout.Token);
            }
        }
        catch
        {
            // Disposal remains best-effort when the peer has already gone away.
        }
        finally
        {
            socket.Dispose();
        }
    }
}
