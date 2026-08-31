using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.NFS.Protocol;

internal enum NfsHandleKind : byte
{
    Root = 0x00,
    Directory = 0x01,
    File = 0x02,
}

internal sealed record NfsFileHandle(NfsHandleKind Kind, Guid EntryId)
{
    private const byte VersionByte = 0xFE;
    private const int EncodedLength = 18;

    public static NfsFileHandle Root { get; } = new(NfsHandleKind.Root, Guid.Empty);

    public byte[] ToBytes()
    {
        if (Kind == NfsHandleKind.Root && EntryId != Guid.Empty)
            throw new InvalidOperationException("The root NFS handle must use the empty entry id.");
        if (Kind != NfsHandleKind.Root && EntryId == Guid.Empty)
            throw new InvalidOperationException("A non-root NFS handle must have a stable entry id.");

        var encoded = new byte[EncodedLength];
        encoded[0] = VersionByte;
        encoded[1] = (byte)Kind;
        EntryId.TryWriteBytes(encoded.AsSpan(2), bigEndian: true, out _);
        return encoded;
    }

    public static NfsFileHandle FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != EncodedLength)
            throw new XdrException($"NFS handle must be exactly {EncodedLength} bytes ({bytes.Length} received)");
        if (bytes[0] != VersionByte)
            throw new XdrException($"NFS handle missing version byte (0x{bytes[0]:X2})");

        var entryId = new Guid(bytes[2..], bigEndian: true);
        return bytes[1] switch
        {
            (byte)NfsHandleKind.Root when entryId == Guid.Empty => Root,
            (byte)NfsHandleKind.Directory when entryId != Guid.Empty =>
                new NfsFileHandle(NfsHandleKind.Directory, entryId),
            (byte)NfsHandleKind.File when entryId != Guid.Empty =>
                new NfsFileHandle(NfsHandleKind.File, entryId),
            (byte)NfsHandleKind.Root =>
                throw new XdrException("The root NFS handle has a non-empty entry id"),
            (byte)NfsHandleKind.Directory or (byte)NfsHandleKind.File =>
                throw new XdrException("A non-root NFS handle has an empty entry id"),
            var kind => throw new XdrException($"Unknown NFS handle kind 0x{kind:X2}")
        };
    }
}
