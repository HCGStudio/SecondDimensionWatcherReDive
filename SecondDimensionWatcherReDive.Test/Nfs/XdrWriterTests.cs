using System.Buffers;
using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.Test.Nfs;

[TestClass]
public class XdrWriterTests
{
    [TestMethod]
    public void WriteUInt32_BigEndian()
    {
        var buf = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(buf);
        w.WriteUInt32(0x12345678);
        CollectionAssert.AreEqual(new byte[] { 0x12, 0x34, 0x56, 0x78 }, buf.WrittenSpan.ToArray());
    }

    [TestMethod]
    public void WriteUInt64_BigEndian()
    {
        var buf = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(buf);
        w.WriteUInt64(0x0102030405060708ul);
        CollectionAssert.AreEqual(
            new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, buf.WrittenSpan.ToArray());
    }

    [TestMethod]
    public void WriteBool()
    {
        var buf = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(buf);
        w.WriteBool(true);
        w.WriteBool(false);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 1, 0, 0, 0, 0 }, buf.WrittenSpan.ToArray());
    }

    [TestMethod]
    public void WriteOpaque_PadsTo4Bytes()
    {
        var buf = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(buf);
        w.WriteOpaque([1, 2, 3]);
        CollectionAssert.AreEqual(
            new byte[] { 0, 0, 0, 3, 1, 2, 3, 0 }, buf.WrittenSpan.ToArray());
    }

    [TestMethod]
    public void WriteOpaque_AlignedNeedsNoPad()
    {
        var buf = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(buf);
        w.WriteOpaque([1, 2, 3, 4]);
        CollectionAssert.AreEqual(
            new byte[] { 0, 0, 0, 4, 1, 2, 3, 4 }, buf.WrittenSpan.ToArray());
    }

    [TestMethod]
    public void WriteString_Utf8()
    {
        var buf = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(buf);
        w.WriteString("hi");
        CollectionAssert.AreEqual(
            new byte[] { 0, 0, 0, 2, (byte)'h', (byte)'i', 0, 0 }, buf.WrittenSpan.ToArray());
    }

    [TestMethod]
    public void WriteUInt32Array()
    {
        var buf = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(buf);
        w.WriteUInt32Array([1, 2]);
        CollectionAssert.AreEqual(
            new byte[]
            {
                0, 0, 0, 2,
                0, 0, 0, 1,
                0, 0, 0, 2
            },
            buf.WrittenSpan.ToArray());
    }

    [TestMethod]
    public void WriteRaw_NoLengthOrPadding()
    {
        var buf = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(buf);
        w.WriteRaw([0xAA, 0xBB, 0xCC]);
        CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB, 0xCC }, buf.WrittenSpan.ToArray());
    }

    [TestMethod]
    public void RoundTrip_AllPrimitives()
    {
        var buf = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(buf);
        w.WriteUInt32(0x11223344);
        w.WriteInt32(-2);
        w.WriteUInt64(0xDEADBEEFCAFEBABEul);
        w.WriteBool(true);
        w.WriteString("hello world");
        w.WriteOpaque([0xCA, 0xFE]);

        var r = new XdrReader(buf.WrittenSpan);
        Assert.AreEqual(0x11223344u, r.ReadUInt32());
        Assert.AreEqual(-2, r.ReadInt32());
        Assert.AreEqual(0xDEADBEEFCAFEBABEul, r.ReadUInt64());
        Assert.IsTrue(r.ReadBool());
        Assert.AreEqual("hello world", r.ReadString());
        var bytes = r.ReadOpaque();
        Assert.AreEqual(2, bytes.Length);
        Assert.AreEqual(0xCA, bytes[0]);
        Assert.AreEqual(0xFE, bytes[1]);
    }
}
