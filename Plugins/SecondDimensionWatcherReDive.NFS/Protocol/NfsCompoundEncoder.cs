using SecondDimensionWatcherReDive.NFS.Protocol;
using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.NFS.Server;

internal sealed record NfsOpResult(uint OpCode, byte[] Body);

internal sealed record NfsCompoundResult(uint Status, IReadOnlyList<NfsOpResult> Results);

internal static class NfsCompoundEncoder
{
    public static void Write(XdrWriter writer, string tag, NfsCompoundResult result)
    {
        writer.WriteUInt32(result.Status);
        writer.WriteString(tag);
        writer.WriteUInt32((uint)result.Results.Count);
        foreach (var entry in result.Results)
        {
            writer.WriteUInt32(entry.OpCode);
            writer.WriteRaw(entry.Body);
        }
    }
}
