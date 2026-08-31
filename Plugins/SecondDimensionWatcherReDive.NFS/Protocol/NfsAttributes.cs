using System.Buffers;
using System.Text;
using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.NFS.Protocol;

internal static class NfsAttributes
{
    private static readonly int[] s_supportedAttrs =
    [
        NfsConstants.FattrSupportedAttrs,
        NfsConstants.FattrType,
        NfsConstants.FattrFhExpireType,
        NfsConstants.FattrChange,
        NfsConstants.FattrSize,
        NfsConstants.FattrLinkSupport,
        NfsConstants.FattrSymlinkSupport,
        NfsConstants.FattrNamedAttr,
        NfsConstants.FattrFsId,
        NfsConstants.FattrUniqueHandles,
        NfsConstants.FattrLeaseTime,
        NfsConstants.FattrRdAttrError,
        NfsConstants.FattrFilehandle,
        NfsConstants.FattrFileId,
        NfsConstants.FattrMaxFileSize,
        NfsConstants.FattrMaxName,
        NfsConstants.FattrMaxRead,
        NfsConstants.FattrMode,
        NfsConstants.FattrNumLinks,
        NfsConstants.FattrOwner,
        NfsConstants.FattrOwnerGroup,
        NfsConstants.FattrSpaceUsed,
        NfsConstants.FattrTimeAccess,
        NfsConstants.FattrTimeMetadata,
        NfsConstants.FattrTimeModify,
    ];

    public static IReadOnlyCollection<int> SupportedAttributeIds { get; } = s_supportedAttrs;

    private static readonly HashSet<int> s_supportedSet = new(s_supportedAttrs);

    public static uint[] BitmapFromIds(IEnumerable<int> attrIds)
    {
        var ids = attrIds as IReadOnlyCollection<int> ?? attrIds.ToArray();
        if (ids.Count == 0)
            return [];
        var maxBit = ids.Max();
        var words = (maxBit / 32) + 1;
        var result = new uint[words];
        foreach (var id in ids)
            result[id / 32] |= 1u << (id % 32);
        return result;
    }

    public static int[] IdsFromBitmap(ReadOnlySpan<uint> words)
    {
        var result = new List<int>();
        for (var w = 0; w < words.Length; w++)
        {
            var word = words[w];
            for (var b = 0; b < 32; b++)
            {
                if ((word & (1u << b)) != 0)
                    result.Add(w * 32 + b);
            }
        }
        return result.ToArray();
    }

    public static void EncodeGetAttrResponse(XdrWriter writer, ReadOnlySpan<uint> requestBitmap, AttrSource source)
    {
        var requested = IdsFromBitmap(requestBitmap);
        var supported = requested.Where(s_supportedSet.Contains).OrderBy(x => x).ToArray();
        var responseBitmap = BitmapFromIds(supported);
        writer.WriteUInt32Array(responseBitmap);

        var inner = new ArrayBufferWriter<byte>();
        var innerWriter = new XdrWriter(inner);
        foreach (var id in supported)
            EncodeAttribute(innerWriter, id, source);
        writer.WriteOpaque(inner.WrittenSpan);
    }

    private static void EncodeAttribute(XdrWriter writer, int id, AttrSource source)
    {
        switch (id)
        {
            case NfsConstants.FattrSupportedAttrs:
                writer.WriteUInt32Array(BitmapFromIds(s_supportedAttrs));
                break;
            case NfsConstants.FattrType:
                writer.WriteUInt32(source.IsDirectory ? NfsConstants.Nf4Dir : NfsConstants.Nf4Reg);
                break;
            case NfsConstants.FattrFhExpireType:
                writer.WriteUInt32(NfsConstants.Fh4Persistent);
                break;
            case NfsConstants.FattrChange:
                writer.WriteUInt64((ulong)source.MTime.UtcTicks);
                break;
            case NfsConstants.FattrSize:
                writer.WriteUInt64((ulong)source.Size);
                break;
            case NfsConstants.FattrLinkSupport:
            case NfsConstants.FattrSymlinkSupport:
            case NfsConstants.FattrNamedAttr:
                writer.WriteBool(false);
                break;
            case NfsConstants.FattrFsId:
                writer.WriteUInt64(NfsConstants.FsIdMajor);
                writer.WriteUInt64(NfsConstants.FsIdMinor);
                break;
            case NfsConstants.FattrUniqueHandles:
                writer.WriteBool(true);
                break;
            case NfsConstants.FattrLeaseTime:
                writer.WriteUInt32((uint)source.LeaseTimeSeconds);
                break;
            case NfsConstants.FattrRdAttrError:
                writer.WriteUInt32(NfsConstants.Ok);
                break;
            case NfsConstants.FattrFilehandle:
                writer.WriteOpaque(source.Handle.ToBytes());
                break;
            case NfsConstants.FattrFileId:
                writer.WriteUInt64(
                    source.CanonicalFileId ?? StableHash(source.Handle.ToBytes()));
                break;
            case NfsConstants.FattrMaxFileSize:
                writer.WriteUInt64((ulong)NfsConstants.MaxFileSize);
                break;
            case NfsConstants.FattrMaxName:
                writer.WriteUInt32((uint)NfsConstants.MaxName);
                break;
            case NfsConstants.FattrMaxRead:
                writer.WriteUInt64((ulong)NfsConstants.MaxRead);
                break;
            case NfsConstants.FattrMode:
                // 0555 octal (r-x for all) for dirs, 0444 (r-- for all) for files
                writer.WriteUInt32(source.IsDirectory ? 0x16Du : 0x124u);
                break;
            case NfsConstants.FattrNumLinks:
                writer.WriteUInt32(1);
                break;
            case NfsConstants.FattrOwner:
                writer.WriteString(source.OwnerName);
                break;
            case NfsConstants.FattrOwnerGroup:
                writer.WriteString(source.GroupName);
                break;
            case NfsConstants.FattrSpaceUsed:
                writer.WriteUInt64((ulong)source.Size);
                break;
            case NfsConstants.FattrTimeAccess:
            case NfsConstants.FattrTimeMetadata:
            case NfsConstants.FattrTimeModify:
                WriteTime(writer, source.MTime);
                break;
            default:
                throw new InvalidOperationException($"Unsupported attribute id {id}");
        }
    }

    private static void WriteTime(XdrWriter writer, DateTimeOffset time)
    {
        var unixSeconds = time.ToUnixTimeSeconds();
        var secondsTicks = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcTicks;
        var nanos = (uint)((time.UtcTicks - secondsTicks) * 100);
        writer.WriteInt64(unixSeconds);
        writer.WriteUInt32(nanos);
    }

    internal static ulong ComputeCanonicalFileId(
        NfsHandleKind kind,
        string canonicalVirtualPath)
    {
        if (kind == NfsHandleKind.Root)
            return StableHash([(byte)0xFE, (byte)NfsHandleKind.Root]);

        var pathBytes = Encoding.UTF8.GetBytes(canonicalVirtualPath);
        var legacyIdentity = new byte[pathBytes.Length + 2];
        legacyIdentity[0] = 0xFE;
        legacyIdentity[1] = (byte)kind;
        pathBytes.CopyTo(legacyIdentity, 2);
        return StableHash(legacyIdentity);
    }

    private static ulong StableHash(ReadOnlySpan<byte> value)
    {
        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime = 1099511628211UL;
        var h = fnvOffset;
        foreach (var item in value)
        {
            h ^= item;
            h *= fnvPrime;
        }
        return h;
    }
}
