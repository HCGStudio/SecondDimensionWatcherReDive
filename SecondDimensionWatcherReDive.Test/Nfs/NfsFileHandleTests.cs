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
        var decoded = NfsFileHandle.FromBytes(bytes);
        Assert.AreSame(NfsFileHandle.Root, decoded);
    }

    [TestMethod]
    public void File_RoundTrips()
    {
        var fh = new NfsFileHandle(
            NfsHandleKind.File,
            Guid.Parse("11111111-2222-3333-4444-555555555555"));
        var bytes = fh.ToBytes();
        Assert.AreEqual(0xFE, bytes[0]);
        Assert.AreEqual(0x02, bytes[1]);

        var decoded = NfsFileHandle.FromBytes(bytes);
        Assert.AreEqual(fh, decoded);
    }

    [TestMethod]
    public void Directory_RoundTrips()
    {
        var fh = new NfsFileHandle(
            NfsHandleKind.Directory,
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        var bytes = fh.ToBytes();
        Assert.AreEqual(0x01, bytes[1]);

        var decoded = NfsFileHandle.FromBytes(bytes);
        Assert.AreEqual(fh, decoded);
    }

    [TestMethod]
    public void MalformedPrefixThrows()
    {
        Assert.ThrowsExactly<XdrException>(() =>
            NfsFileHandle.FromBytes([0xAB, 0x01, .. new byte[16]]));
    }

    [TestMethod]
    public void UnknownKindThrows()
    {
        Assert.ThrowsExactly<XdrException>(() =>
            NfsFileHandle.FromBytes([0xFE, 0x09, .. Guid.NewGuid().ToByteArray(true)]));
    }

    [TestMethod]
    public void TooShortThrows()
    {
        Assert.ThrowsExactly<XdrException>(() =>
            NfsFileHandle.FromBytes(new byte[] { 0xFE }));
    }
}
