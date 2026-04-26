using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace SecondDimensionWatcherReDive.NFS.Xdr;

internal sealed class XdrWriter
{
    private readonly IBufferWriter<byte> _output;

    public XdrWriter(IBufferWriter<byte> output)
    {
        _output = output;
    }

    public int BytesWritten { get; private set; }

    public void WriteUInt32(uint value)
    {
        var span = _output.GetSpan(4);
        BinaryPrimitives.WriteUInt32BigEndian(span, value);
        _output.Advance(4);
        BytesWritten += 4;
    }

    public void WriteInt32(int value) => WriteUInt32(unchecked((uint)value));

    public void WriteUInt64(ulong value)
    {
        var span = _output.GetSpan(8);
        BinaryPrimitives.WriteUInt64BigEndian(span, value);
        _output.Advance(8);
        BytesWritten += 8;
    }

    public void WriteInt64(long value) => WriteUInt64(unchecked((ulong)value));

    public void WriteBool(bool value) => WriteUInt32(value ? 1u : 0u);

    public void WriteOpaque(ReadOnlySpan<byte> value)
    {
        WriteUInt32((uint)value.Length);
        WriteFixedOpaque(value);
    }

    public void WriteFixedOpaque(ReadOnlySpan<byte> value)
    {
        if (value.Length > 0)
        {
            var span = _output.GetSpan(value.Length);
            value.CopyTo(span);
            _output.Advance(value.Length);
            BytesWritten += value.Length;
        }
        var pad = (4 - (value.Length & 3)) & 3;
        if (pad > 0)
        {
            var padSpan = _output.GetSpan(pad);
            padSpan[..pad].Clear();
            _output.Advance(pad);
            BytesWritten += pad;
        }
    }

    public void WriteString(string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteUInt32((uint)byteCount);
        if (byteCount == 0)
        {
            return;
        }
        if (byteCount <= 256)
        {
            Span<byte> stack = stackalloc byte[byteCount];
            var written = Encoding.UTF8.GetBytes(value, stack);
            WriteFixedOpaque(stack[..written]);
        }
        else
        {
            var rented = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                var written = Encoding.UTF8.GetBytes(value, rented);
                WriteFixedOpaque(rented.AsSpan(0, written));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    public void WriteUInt32Array(ReadOnlySpan<uint> values)
    {
        WriteUInt32((uint)values.Length);
        foreach (var v in values)
            WriteUInt32(v);
    }

    public void WriteRaw(ReadOnlySpan<byte> value)
    {
        if (value.Length == 0)
            return;
        var span = _output.GetSpan(value.Length);
        value.CopyTo(span);
        _output.Advance(value.Length);
        BytesWritten += value.Length;
    }
}
