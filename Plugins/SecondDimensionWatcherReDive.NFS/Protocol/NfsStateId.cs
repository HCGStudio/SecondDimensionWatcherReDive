using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.NFS.Protocol;

internal readonly record struct NfsStateId(uint SeqId, ulong OtherHi, uint OtherLo)
{
    public static NfsStateId AnyState { get; } = default;

    public bool IsAny => SeqId == 0 && OtherHi == 0 && OtherLo == 0;

    public void WriteTo(XdrWriter writer)
    {
        writer.WriteUInt32(SeqId);
        writer.WriteUInt64(OtherHi);
        writer.WriteUInt32(OtherLo);
    }

    public static NfsStateId Read(ref XdrReader reader)
    {
        var seqId = reader.ReadUInt32();
        var hi = reader.ReadUInt64();
        var lo = reader.ReadUInt32();
        return new NfsStateId(seqId, hi, lo);
    }
}
