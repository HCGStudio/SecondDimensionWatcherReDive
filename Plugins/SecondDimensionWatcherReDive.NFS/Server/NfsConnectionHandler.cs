using System.Buffers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecondDimensionWatcherReDive.NFS.Auth;
using SecondDimensionWatcherReDive.NFS.Protocol;
using SecondDimensionWatcherReDive.NFS.Rpc;
using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.NFS.Server;

internal sealed partial class NfsConnectionHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<NfsConnectionHandler> logger,
    NfsOptions options)
{
    public async Task RunAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(options.IdleTimeoutSeconds));
                try
                {
                    using var record = await RpcRecordReader.ReadAsync(
                        stream, RpcConstants.MaxRequestBytes, requestTimeout.Token);
                    if (record is null)
                        return;

                    await HandleRequestAsync(stream, record.Memory, requestTimeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    LogRequestDeadlineExceeded(logger, options.IdleTimeoutSeconds);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (EndOfStreamException)
        {
        }
        catch (Exception ex)
        {
            LogConnectionFailed(logger, ex);
        }
    }

    private async Task HandleRequestAsync(
        Stream stream,
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken)
    {
        var reply = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(reply);

        try
        {
            var (header, bodyOffset) = RpcDecoder.DecodeCall(message.Span, options.AllowAnonymous);

            if (header.Program != NfsConstants.NfsProgram)
            {
                RpcEncoder.WriteAcceptedErrorHeader(writer, header.Xid, RpcConstants.ProgUnavail);
            }
            else if (header.Version != NfsConstants.NfsV4)
            {
                RpcEncoder.WriteProgramMismatchHeader(
                    writer, header.Xid, NfsConstants.NfsV4, NfsConstants.NfsV4);
            }
            else
            {
                switch (header.Procedure)
                {
                    case RpcConstants.NfsProcNull:
                        RpcEncoder.WriteAcceptedSuccessHeader(writer, header.Xid);
                        break;
                    case RpcConstants.NfsProcCompound:
                        await DispatchCompoundAsync(
                            writer, header, message[bodyOffset..], cancellationToken);
                        break;
                    default:
                        RpcEncoder.WriteAcceptedErrorHeader(
                            writer, header.Xid, RpcConstants.ProcUnavail);
                        break;
                }
            }
        }
        catch (RpcAuthRejectedException)
        {
            var xid = TryReadXid(message.Span);
            reply = new ArrayBufferWriter<byte>();
            writer = new XdrWriter(reply);
            RpcEncoder.WriteAuthErrorHeader(writer, xid, RpcConstants.AuthRejectedCred);
        }
        catch (RpcMalformedException ex)
        {
            LogMalformedCall(logger, ex);
            return;
        }
        catch (XdrException ex)
        {
            LogMalformedCall(logger, ex);
            var xid = TryReadXid(message.Span);
            reply = new ArrayBufferWriter<byte>();
            writer = new XdrWriter(reply);
            RpcEncoder.WriteAcceptedErrorHeader(writer, xid, RpcConstants.GarbageArgs);
        }

        await RpcRecordReader.WriteAsync(stream, reply.WrittenMemory, cancellationToken);
    }

    private async Task DispatchCompoundAsync(
        XdrWriter writer,
        RpcCallHeader header,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        NfsCompoundRequest request;
        try
        {
            request = NfsCompoundDecoder.Decode(body.Span);
        }
        catch (XdrException ex)
        {
            LogMalformedCall(logger, ex);
            RpcEncoder.WriteAcceptedSuccessHeader(writer, header.Xid);
            var emptyResult = new NfsCompoundResult(NfsConstants.ErrBadXdr, []);
            NfsCompoundEncoder.Write(writer, string.Empty, emptyResult);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<NfsCompoundDispatcher>();
        var context = new NfsRequestContext
        {
            Credential = header.Credential,
            CancellationToken = cancellationToken,
        };

        var result = await dispatcher.DispatchAsync(request, context);

        RpcEncoder.WriteAcceptedSuccessHeader(writer, header.Xid);
        NfsCompoundEncoder.Write(writer, request.Tag, result);
    }

    private static uint TryReadXid(ReadOnlySpan<byte> message)
    {
        if (message.Length < 4)
            return 0;
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(message);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "NFS connection terminated unexpectedly")]
    private static partial void LogConnectionFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Malformed NFS call discarded")]
    private static partial void LogMalformedCall(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "NFS connection closed after its {RequestTimeoutSeconds} second request deadline")]
    private static partial void LogRequestDeadlineExceeded(ILogger logger, int requestTimeoutSeconds);
}
