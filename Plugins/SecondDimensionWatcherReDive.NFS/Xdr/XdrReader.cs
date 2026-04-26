using System.Buffers.Binary;
using System.Text;

namespace SecondDimensionWatcherReDive.NFS.Xdr;

internal ref struct XdrReader
{
    private ReadOnlySpan<byte> _buffer;

    public XdrReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        Consumed = 0;
    }

    public int Consumed { get; private set; }

    public int Remaining => _buffer.Length;

    public uint ReadUInt32()
    {
        if (_buffer.Length < 4)
            throw new XdrException($"Truncated uint32 (need 4 bytes, have {_buffer.Length})");
        var value = BinaryPrimitives.ReadUInt32BigEndian(_buffer);
        _buffer = _buffer[4..];
        Consumed += 4;
        return value;
    }

    public int ReadInt32() => unchecked((int)ReadUInt32());

    public ulong ReadUInt64()
    {
        if (_buffer.Length < 8)
            throw new XdrException($"Truncated uint64 (need 8 bytes, have {_buffer.Length})");
        var value = BinaryPrimitives.ReadUInt64BigEndian(_buffer);
        _buffer = _buffer[8..];
        Consumed += 8;
        return value;
    }

    public long ReadInt64() => unchecked((long)ReadUInt64());

    public bool ReadBool() => ReadUInt32() switch
    {
        0 => false,
        1 => true,
        var v => throw new XdrException($"Bool value must be 0 or 1, got {v}")
    };

    public ReadOnlySpan<byte> ReadOpaque()
    {
        var length = ReadUInt32();
        if (length > int.MaxValue)
            throw new XdrException($"Opaque length {length} exceeds Int32.MaxValue");
        return ReadFixedOpaque((int)length);
    }

    public ReadOnlySpan<byte> ReadFixedOpaque(int length)
    {
        if (length < 0)
            throw new XdrException("Negative opaque length");
        if (_buffer.Length < length)
            throw new XdrException($"Truncated opaque (need {length} bytes, have {_buffer.Length})");
        var data = _buffer[..length];
        var padded = (length + 3) & ~3;
        if (_buffer.Length < padded)
            throw new XdrException($"Truncated opaque padding (need {padded} bytes, have {_buffer.Length})");
        _buffer = _buffer[padded..];
        Consumed += padded;
        return data;
    }

    public string ReadString()
    {
        var bytes = ReadOpaque();
        return bytes.IsEmpty ? string.Empty : Encoding.UTF8.GetString(bytes);
    }

    public uint[] ReadUInt32Array()
    {
        var count = ReadUInt32();
        if (count > (uint)(_buffer.Length / 4) + 1)
            throw new XdrException($"Implausible uint32 array length {count}");
        var result = new uint[count];
        for (var i = 0; i < count; i++)
            result[i] = ReadUInt32();
        return result;
    }

    public void Skip(int bytes)
    {
        if (bytes < 0)
            throw new XdrException("Cannot skip a negative number of bytes");
        if (_buffer.Length < bytes)
            throw new XdrException($"Truncated skip (need {bytes} bytes, have {_buffer.Length})");
        _buffer = _buffer[bytes..];
        Consumed += bytes;
    }
}
