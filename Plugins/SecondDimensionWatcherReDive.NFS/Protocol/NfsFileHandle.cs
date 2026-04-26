using System.Text;
using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.NFS.Protocol;

internal enum NfsHandleKind : byte
{
    Root = 0x00,
    Directory = 0x01,
    File = 0x02,
}

internal sealed record NfsFileHandle(NfsHandleKind Kind, string VirtualPath)
{
    public static NfsFileHandle Root { get; } = new(NfsHandleKind.Root, "/");

    private const byte VersionByte = 0xFE;

    public byte[] ToBytes()
    {
        if (Kind == NfsHandleKind.Root)
            return [VersionByte, (byte)NfsHandleKind.Root];

        var pathBytes = Encoding.UTF8.GetBytes(VirtualPath);
        var encoded = new byte[2 + pathBytes.Length];
        encoded[0] = VersionByte;
        encoded[1] = (byte)Kind;
        pathBytes.CopyTo(encoded, 2);
        if (encoded.Length > NfsConstants.Fh4MaxSize)
            throw new InvalidOperationException(
                $"Encoded NFS handle exceeds {NfsConstants.Fh4MaxSize} bytes ({encoded.Length})");
        return encoded;
    }

    public static NfsFileHandle FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2)
            throw new XdrException($"NFS handle too short ({bytes.Length} bytes)");
        if (bytes[0] != VersionByte)
            throw new XdrException($"NFS handle missing version byte (0x{bytes[0]:X2})");

        return bytes[1] switch
        {
            (byte)NfsHandleKind.Root => Root,
            (byte)NfsHandleKind.Directory => new NfsFileHandle(
                NfsHandleKind.Directory, Encoding.UTF8.GetString(bytes[2..])),
            (byte)NfsHandleKind.File => new NfsFileHandle(
                NfsHandleKind.File, Encoding.UTF8.GetString(bytes[2..])),
            var k => throw new XdrException($"Unknown NFS handle kind 0x{k:X2}")
        };
    }
}
