using System.Text;
using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.NFS.Protocol;

internal enum NfsHandleKind : byte
{
    Root = 0x00,
    Directory = 0x01,
    File = 0x02,
}

internal sealed record NfsFileHandle(
    NfsHandleKind Kind,
    Guid EntryId,
    string? LegacyVirtualPath = null,
    bool UsesStableEntryVersion = false)
{
    private const byte LegacyPathVersionByte = 0xFE;
    private const byte StableEntryVersionByte = 0xFF;
    private const int StableEntryEncodedLength = 18;
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);

    // Root has no path/entry identity ambiguity, so retain its historical wire
    // shape and singleton identity while versioning every non-root stable handle.
    public static NfsFileHandle Root { get; } = new(NfsHandleKind.Root, Guid.Empty);
    public static NfsFileHandle StableRoot { get; } = new(
        NfsHandleKind.Root,
        Guid.Empty,
        UsesStableEntryVersion: true);

    public static NfsFileHandle ForStableEntry(NfsHandleKind kind, Guid entryId) =>
        kind == NfsHandleKind.Root
            ? Root
            : new NfsFileHandle(kind, entryId, UsesStableEntryVersion: true);

    public byte[] ToBytes()
    {
        if (LegacyVirtualPath is not null)
            return EncodeLegacyPath();
        if (Kind == NfsHandleKind.Root && EntryId != Guid.Empty)
            throw new InvalidOperationException("The root NFS handle must use the empty entry id.");
        if (Kind != NfsHandleKind.Root && EntryId == Guid.Empty)
            throw new InvalidOperationException("A non-root NFS handle must have a stable entry id.");

        var encoded = new byte[StableEntryEncodedLength];
        encoded[0] = UsesStableEntryVersion
            ? StableEntryVersionByte
            : LegacyPathVersionByte;
        encoded[1] = (byte)Kind;
        EntryId.TryWriteBytes(encoded.AsSpan(2), bigEndian: true, out _);
        return encoded;
    }

    public static NfsFileHandle FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2 || bytes.Length > NfsConstants.Fh4MaxSize)
            throw new XdrException($"Invalid NFS handle length ({bytes.Length} bytes)");

        return bytes[0] switch
        {
            StableEntryVersionByte => DecodeStableEntry(bytes),
            LegacyPathVersionByte => DecodeLegacyOrTransitional(bytes),
            var version => throw new XdrException(
                $"Unsupported NFS handle version byte (0x{version:X2})")
        };
    }

    private static NfsFileHandle DecodeStableEntry(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != StableEntryEncodedLength)
            throw new XdrException(
                $"Stable NFS handle must be exactly {StableEntryEncodedLength} bytes ({bytes.Length} received)");

        var entryId = new Guid(bytes[2..], bigEndian: true);
        return bytes[1] switch
        {
            (byte)NfsHandleKind.Root when entryId == Guid.Empty => StableRoot,
            (byte)NfsHandleKind.Directory when entryId != Guid.Empty =>
                ForStableEntry(NfsHandleKind.Directory, entryId),
            (byte)NfsHandleKind.File when entryId != Guid.Empty =>
                ForStableEntry(NfsHandleKind.File, entryId),
            (byte)NfsHandleKind.Root =>
                throw new XdrException("The root NFS handle has a non-empty entry id"),
            (byte)NfsHandleKind.Directory or (byte)NfsHandleKind.File =>
                throw new XdrException("A non-root NFS handle has an empty entry id"),
            var kind => throw new XdrException($"Unknown NFS handle kind 0x{kind:X2}")
        };
    }

    private static NfsFileHandle DecodeLegacyOrTransitional(ReadOnlySpan<byte> bytes)
    {
        if (bytes[1] == (byte)NfsHandleKind.Root)
        {
            if (bytes.Length == 2) return Root;
            if (bytes.Length == StableEntryEncodedLength && IsEmpty(bytes[2..]))
                return Root;
            throw new XdrException("A legacy root NFS handle contains trailing data");
        }

        if (bytes[1] is not ((byte)NfsHandleKind.Directory) and not ((byte)NfsHandleKind.File))
            throw new XdrException($"Unknown NFS handle kind 0x{bytes[1]:X2}");
        if (bytes.Length == 2)
            throw new XdrException("A legacy non-root NFS handle has an empty path");

        if (TryDecodeLegacyPath(bytes[2..], out var virtualPath))
            return new NfsFileHandle((NfsHandleKind)bytes[1], Guid.Empty, virtualPath);

        // A short-lived pre-version implementation emitted stable GUID handles
        // with the legacy marker. Accept those where they cannot be a valid path.
        if (bytes.Length == StableEntryEncodedLength)
        {
            var entryId = new Guid(bytes[2..], bigEndian: true);
            if (entryId != Guid.Empty)
                return new NfsFileHandle((NfsHandleKind)bytes[1], entryId);
        }

        throw new XdrException("A legacy NFS handle contains an invalid virtual path");
    }

    private static bool TryDecodeLegacyPath(ReadOnlySpan<byte> bytes, out string virtualPath)
    {
        virtualPath = string.Empty;
        try
        {
            virtualPath = s_strictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        if (virtualPath.Length < 2 || virtualPath[0] != '/' || virtualPath[^1] == '/')
            return false;

        return true;
    }

    private byte[] EncodeLegacyPath()
    {
        if (Kind == NfsHandleKind.Root
            || EntryId != Guid.Empty
            || LegacyVirtualPath is null
            || UsesStableEntryVersion)
            throw new InvalidOperationException("The legacy NFS handle state is invalid.");

        var pathBytes = s_strictUtf8.GetBytes(LegacyVirtualPath);
        var encoded = new byte[2 + pathBytes.Length];
        if (encoded.Length > NfsConstants.Fh4MaxSize)
            throw new InvalidOperationException(
                $"Encoded NFS handle exceeds {NfsConstants.Fh4MaxSize} bytes ({encoded.Length})");
        encoded[0] = LegacyPathVersionByte;
        encoded[1] = (byte)Kind;
        pathBytes.CopyTo(encoded, 2);
        return encoded;
    }

    private static bool IsEmpty(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value != 0) return false;
        }

        return true;
    }
}
