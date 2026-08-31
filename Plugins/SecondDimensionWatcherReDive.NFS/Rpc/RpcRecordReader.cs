using System.Buffers;
using System.Buffers.Binary;

namespace SecondDimensionWatcherReDive.NFS.Rpc;

internal sealed class RpcRecord : IDisposable
{
    private byte[]? _buffer;
    private readonly int _length;

    internal RpcRecord(byte[] buffer, int length)
    {
        _buffer = buffer;
        _length = length;
    }

    public ReadOnlyMemory<byte> Memory =>
        _buffer is null
            ? throw new ObjectDisposedException(nameof(RpcRecord))
            : _buffer.AsMemory(0, _length);

    public ReadOnlySpan<byte> Span =>
        _buffer is null
            ? throw new ObjectDisposedException(nameof(RpcRecord))
            : _buffer.AsSpan(0, _length);

    public int Length => _length;

    public void Dispose()
    {
        var buf = Interlocked.Exchange(ref _buffer, null);
        if (buf is not null)
            ArrayPool<byte>.Shared.Return(buf);
    }
}

internal static class RpcRecordReader
{
    public static async ValueTask<RpcRecord?> ReadAsync(
        Stream stream,
        int maxRecordBytes,
        CancellationToken cancellationToken)
    {
        var totalLength = 0;
        var fragmentCount = 0;
        byte[]? buffer = null;
        var success = false;
        var headerBuf = new byte[RpcConstants.RecordHeaderSize];
        try
        {
            while (true)
            {
                var read = await ReadExactAsync(stream, headerBuf, cancellationToken).ConfigureAwait(false);
                if (read == 0 && totalLength == 0)
                    return null;
                if (read != headerBuf.Length)
                    throw new EndOfStreamException("RPC fragment header truncated");

                var header = BinaryPrimitives.ReadUInt32BigEndian(headerBuf);
                var isLast = (header & RpcConstants.LastFragmentMask) != 0;
                var fragmentLength = (int)(header & RpcConstants.LengthMask);

                fragmentCount++;
                if (fragmentCount > RpcConstants.MaxFragmentsPerRecord)
                    throw new InvalidDataException(
                        $"RPC record exceeds the fragment limit ({RpcConstants.MaxFragmentsPerRecord}).");
                if (!isLast && fragmentLength == 0)
                    throw new InvalidDataException("RPC record contains a non-final empty fragment.");

                if (totalLength + fragmentLength > maxRecordBytes)
                    throw new InvalidDataException(
                        $"RPC record exceeds limit ({totalLength + fragmentLength} > {maxRecordBytes})");

                buffer = EnsureCapacity(buffer, totalLength + fragmentLength);
                if (fragmentLength > 0)
                {
                    var slice = buffer.AsMemory(totalLength, fragmentLength);
                    var n = await ReadExactAsync(stream, slice, cancellationToken).ConfigureAwait(false);
                    if (n != fragmentLength)
                        throw new EndOfStreamException("RPC fragment payload truncated");
                    totalLength += fragmentLength;
                }

                if (isLast)
                {
                    var owned = buffer ?? ArrayPool<byte>.Shared.Rent(0);
                    var record = new RpcRecord(owned, totalLength);
                    success = true;
                    return record;
                }
            }
        }
        finally
        {
            if (!success && buffer is not null)
                ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async ValueTask WriteAsync(
        Stream stream,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var headerBuf = new byte[RpcConstants.RecordHeaderSize];
        var header = RpcConstants.LastFragmentMask | (uint)payload.Length;
        BinaryPrimitives.WriteUInt32BigEndian(headerBuf, header);
        await stream.WriteAsync(headerBuf, cancellationToken).ConfigureAwait(false);
        if (!payload.IsEmpty)
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static byte[] EnsureCapacity(byte[]? buffer, int needed)
    {
        if (buffer is not null && buffer.Length >= needed)
            return buffer;
        var grown = ArrayPool<byte>.Shared.Rent(Math.Max(needed, buffer?.Length * 2 ?? 4096));
        if (buffer is not null)
        {
            buffer.AsSpan().CopyTo(grown);
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return grown;
    }

    private static async ValueTask<int> ReadExactAsync(
        Stream stream,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < destination.Length)
        {
            var n = await stream.ReadAsync(destination[total..], cancellationToken).ConfigureAwait(false);
            if (n == 0)
                break;
            total += n;
        }
        return total;
    }
}
