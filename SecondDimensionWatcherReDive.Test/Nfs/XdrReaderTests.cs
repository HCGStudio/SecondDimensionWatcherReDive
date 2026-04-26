using System.Buffers;
using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.Test.Nfs;

[TestClass]
public class XdrReaderTests
{
    [TestMethod]
    public void ReadUInt32_BigEndian()
    {
        ReadOnlySpan<byte> data = [0x00, 0x00, 0x01, 0x02];
        var reader = new XdrReader(data);
        Assert.AreEqual(0x102u, reader.ReadUInt32());
        Assert.AreEqual(4, reader.Consumed);
    }

    [TestMethod]
    public void ReadInt32_NegativeWrap()
    {
        ReadOnlySpan<byte> data = [0xFF, 0xFF, 0xFF, 0xFF];
        var reader = new XdrReader(data);
        Assert.AreEqual(-1, reader.ReadInt32());
    }

    [TestMethod]
    public void ReadUInt64_BigEndian()
    {
        ReadOnlySpan<byte> data = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x12, 0x34];
        var reader = new XdrReader(data);
        Assert.AreEqual(0x1234ul, reader.ReadUInt64());
        Assert.AreEqual(8, reader.Consumed);
    }

    [TestMethod]
    public void ReadBool_Valid()
    {
        ReadOnlySpan<byte> bytesTrue = [0, 0, 0, 1];
        ReadOnlySpan<byte> bytesFalse = [0, 0, 0, 0];
        var t = new XdrReader(bytesTrue);
        var f = new XdrReader(bytesFalse);
        Assert.IsTrue(t.ReadBool());
        Assert.IsFalse(f.ReadBool());
    }

    [TestMethod]
    public void ReadBool_NonBinaryThrows()
    {
        var bytes = new byte[] { 0, 0, 0, 2 };
        try
        {
            var reader = new XdrReader(bytes);
            reader.ReadBool();
            Assert.Fail("expected XdrException");
        }
        catch (XdrException) { }
    }

    [TestMethod]
    public void ReadOpaque_PadsToFourBytes()
    {
        ReadOnlySpan<byte> data =
        [
            0, 0, 0, 5,
            (byte)'h', (byte)'e', (byte)'l', (byte)'l',
            (byte)'o', 0, 0, 0,
            0xCA, 0xFE, 0xBA, 0xBE
        ];
        var reader = new XdrReader(data);
        var slice = reader.ReadOpaque();
        Assert.AreEqual(5, slice.Length);
        Assert.AreEqual('h', (char)slice[0]);
        Assert.AreEqual(12, reader.Consumed);
        Assert.AreEqual(0xCAFEBABEu, reader.ReadUInt32());
    }

    [TestMethod]
    public void ReadString_Utf8()
    {
        var bytes = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(bytes);
        w.WriteString("こんにちは");

        var reader = new XdrReader(bytes.WrittenSpan);
        Assert.AreEqual("こんにちは", reader.ReadString());
    }

    [TestMethod]
    public void ReadString_Empty()
    {
        ReadOnlySpan<byte> data = [0, 0, 0, 0];
        var reader = new XdrReader(data);
        Assert.AreEqual(string.Empty, reader.ReadString());
    }

    [TestMethod]
    public void ReadOpaque_TruncatedThrows()
    {
        var bytes = new byte[] { 0, 0, 0, 8, 1, 2, 3 };
        try
        {
            var reader = new XdrReader(bytes);
            reader.ReadOpaque();
            Assert.Fail("expected XdrException");
        }
        catch (XdrException) { }
    }

    [TestMethod]
    public void ReadUInt32_TruncatedThrows()
    {
        var bytes = new byte[] { 0, 1, 2 };
        try
        {
            var reader = new XdrReader(bytes);
            reader.ReadUInt32();
            Assert.Fail("expected XdrException");
        }
        catch (XdrException) { }
    }

    [TestMethod]
    public void ReadUInt32Array()
    {
        ReadOnlySpan<byte> data =
        [
            0, 0, 0, 3,
            0, 0, 0, 1,
            0, 0, 0, 2,
            0, 0, 0, 3
        ];
        var reader = new XdrReader(data);
        var array = reader.ReadUInt32Array();
        CollectionAssert.AreEqual(new uint[] { 1, 2, 3 }, array);
    }
}
