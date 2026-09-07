using Microsoft.Extensions.DependencyInjection;

namespace SecondDimensionWatcherReDive.Services.Transcoding;

/// <summary>
/// Keeps the scoped file-store implementation alive for as long as its stream.
/// </summary>
internal sealed class ScopeOwnedStream(Stream inner, AsyncServiceScope scope) : Stream
{
    private int _disposed;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken)
        => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
        => inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => inner.Read(buffer);

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
        => inner.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
        => inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void SetLength(long value) => inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
        => inner.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
        => inner.WriteAsync(buffer, cancellationToken);

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
        => inner.WriteAsync(buffer, offset, count, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            try
            {
                inner.Dispose();
            }
            finally
            {
                scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            try
            {
                await inner.DisposeAsync();
            }
            finally
            {
                await scope.DisposeAsync();
            }
        }
        GC.SuppressFinalize(this);
    }
}
