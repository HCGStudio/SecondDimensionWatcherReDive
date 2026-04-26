using System.Text;
using SecondDimensionWatcherReDive.NFS.Protocol;
using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.Test.Nfs;

[TestClass]
public class NfsFileHandleTests
{
    [TestMethod]
    public void Root_RoundTrips()
    {
        var bytes = NfsFileHandle.Root.ToBytes();
        CollectionAssert.AreEqual(new byte[] { 0xFE, 0x00 }, bytes);
        var decoded = NfsFileHandle.FromBytes(bytes);
        Assert.AreSame(NfsFileHandle.Root, decoded);
    }

    [TestMethod]
    public void File_RoundTrips()
    {
        var fh = new NfsFileHandle(NfsHandleKind.File, "/anime-a/sub/01.mkv");
        var bytes = fh.ToBytes();
        Assert.AreEqual(0xFE, bytes[0]);
        Assert.AreEqual(0x02, bytes[1]);
        Assert.AreEqual(fh.VirtualPath, Encoding.UTF8.GetString(bytes[2..]));

        var decoded = NfsFileHandle.FromBytes(bytes);
        Assert.AreEqual(fh, decoded);
    }

    [TestMethod]
    public void Directory_RoundTrips()
    {
        var fh = new NfsFileHandle(NfsHandleKind.Directory, "/anime-b");
        var bytes = fh.ToBytes();
        Assert.AreEqual(0x01, bytes[1]);

        var decoded = NfsFileHandle.FromBytes(bytes);
        Assert.AreEqual(fh, decoded);
    }

    [TestMethod]
    public void OverSizeHandleThrows()
    {
        var path = "/" + new string('a', 200);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new NfsFileHandle(NfsHandleKind.File, path).ToBytes());
    }

    [TestMethod]
    public void MalformedPrefixThrows()
    {
        Assert.ThrowsExactly<XdrException>(() =>
            NfsFileHandle.FromBytes(new byte[] { 0xAB, 0x01 }));
    }

    [TestMethod]
    public void UnknownKindThrows()
    {
        Assert.ThrowsExactly<XdrException>(() =>
            NfsFileHandle.FromBytes(new byte[] { 0xFE, 0x09 }));
    }

    [TestMethod]
    public void TooShortThrows()
    {
        Assert.ThrowsExactly<XdrException>(() =>
            NfsFileHandle.FromBytes(new byte[] { 0xFE }));
    }
}
