using System.Buffers;
using SecondDimensionWatcherReDive.NFS;
using SecondDimensionWatcherReDive.NFS.Protocol;
using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.Test.Nfs;

[TestClass]
public class NfsCompoundDecoderTests
{
    [TestMethod]
    public void Decodes_PutRootFh_GetFh_GetAttr()
    {
        var bitmap = new uint[] { 0x12 };
        var bytes = BuildCompound(
            tag: "tag",
            minorVersion: 0,
            opCount: 3,
            writeOps: w =>
            {
                w.WriteUInt32(NfsConstants.OpPutRootFh);
                w.WriteUInt32(NfsConstants.OpGetFh);
                w.WriteUInt32(NfsConstants.OpGetAttr);
                w.WriteUInt32Array(bitmap);
            });

        var compound = NfsCompoundDecoder.Decode(bytes);
        Assert.AreEqual("tag", compound.Tag);
        Assert.AreEqual(0u, compound.MinorVersion);
        Assert.AreEqual(3, compound.Operations.Count);
        Assert.IsInstanceOfType(compound.Operations[0], typeof(PutRootFhOp));
        Assert.IsInstanceOfType(compound.Operations[1], typeof(GetFhOp));
        var getAttr = (GetAttrOp)compound.Operations[2];
        CollectionAssert.AreEqual(bitmap, getAttr.AttrRequest);
    }

    [TestMethod]
    public void Decodes_PutFh_WithHandle()
    {
        var handleBytes = NfsFileHandle.Root.ToBytes();
        var bytes = BuildCompound("", 0, opCount: 1, w =>
        {
            w.WriteUInt32(NfsConstants.OpPutFh);
            w.WriteOpaque(handleBytes);
        });

        var compound = NfsCompoundDecoder.Decode(bytes);
        var op = (PutFhOp)compound.Operations[0];
        CollectionAssert.AreEqual(handleBytes, op.Handle);
    }

    [TestMethod]
    public void Decodes_Lookup_WithName()
    {
        var bytes = BuildCompound("", 0, opCount: 1, w =>
        {
            w.WriteUInt32(NfsConstants.OpLookup);
            w.WriteString("anime-a");
        });

        var compound = NfsCompoundDecoder.Decode(bytes);
        var op = (LookupOp)compound.Operations[0];
        Assert.AreEqual("anime-a", op.Name);
    }

    [TestMethod]
    public void Decodes_Read()
    {
        var bytes = BuildCompound("", 0, opCount: 1, w =>
        {
            w.WriteUInt32(NfsConstants.OpRead);
            NfsStateId.AnyState.WriteTo(w);
            w.WriteUInt64(42);
            w.WriteUInt32(8192);
        });

        var op = (ReadOp)NfsCompoundDecoder.Decode(bytes).Operations[0];
        Assert.AreEqual(42ul, op.Offset);
        Assert.AreEqual(8192u, op.Count);
        Assert.IsTrue(op.StateId.IsAny);
    }

    [TestMethod]
    public void WriteOp_BecomesUnsupportedWithRoFsStatus()
    {
        var bytes = BuildCompound("", 0, opCount: 1, w =>
        {
            w.WriteUInt32(NfsConstants.OpWrite);
            NfsStateId.AnyState.WriteTo(w);
            w.WriteUInt64(0);
            w.WriteUInt32(0);
            w.WriteOpaque([1, 2, 3]);
        });

        var op = (UnsupportedOp)NfsCompoundDecoder.Decode(bytes).Operations[0];
        Assert.AreEqual(NfsConstants.OpWrite, op.ResolvedOpCode);
        Assert.AreEqual(NfsConstants.ErrRoFs, op.MappedStatus);
    }

    [TestMethod]
    public void UnknownOpThrows()
    {
        var bytes = BuildCompound("", 0, opCount: 1, w =>
        {
            w.WriteUInt32(0xDEAD);
        });

        try
        {
            NfsCompoundDecoder.Decode(bytes);
            Assert.Fail("expected XdrException");
        }
        catch (XdrException) { }
    }

    private static byte[] BuildCompound(string tag, uint minorVersion, int opCount, Action<XdrWriter> writeOps)
    {
        var buf = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(buf);
        w.WriteString(tag);
        w.WriteUInt32(minorVersion);
        w.WriteUInt32((uint)opCount);
        writeOps(w);
        return buf.WrittenSpan.ToArray();
    }
}
