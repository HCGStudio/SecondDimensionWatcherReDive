using System.Buffers;
using SecondDimensionWatcherReDive.NFS.Auth;
using SecondDimensionWatcherReDive.NFS.Rpc;
using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.Test.Nfs;

[TestClass]
public class AuthSysCredTests
{
    [TestMethod]
    public void DecodesAllFields()
    {
        var bytes = BuildAuthSysCred(stamp: 0x100, machineName: "host-01", uid: 7, gid: 8, gids: [9, 10, 11]);
        var reader = new XdrReader(bytes);
        var cred = RpcAuthDecoder.ReadCredential(ref reader);

        Assert.AreEqual(0x100u, cred.Stamp);
        Assert.AreEqual("host-01", cred.MachineName);
        Assert.AreEqual(7u, cred.Uid);
        Assert.AreEqual(8u, cred.Gid);
        CollectionAssert.AreEqual(new uint[] { 9, 10, 11 }, cred.Gids);
    }

    [TestMethod]
    public void TooManyGidsRejected()
    {
        var manyGids = Enumerable.Range(0, 17).Select(i => (uint)i).ToArray();
        var bytes = BuildAuthSysCred(stamp: 0, machineName: "host", uid: 0, gid: 0, gids: manyGids);
        try
        {
            var reader = new XdrReader(bytes);
            RpcAuthDecoder.ReadCredential(ref reader);
            Assert.Fail("expected XdrException");
        }
        catch (XdrException) { }
    }

    [TestMethod]
    public void RpcSecGssRejected()
    {
        var buf = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(buf);
        w.WriteUInt32(RpcConstants.RpcSecGss);
        w.WriteOpaque([1, 2, 3]);

        var bytes = buf.WrittenSpan.ToArray();
        try
        {
            var reader = new XdrReader(bytes);
            RpcAuthDecoder.ReadCredential(ref reader);
            Assert.Fail("expected RpcAuthRejectedException");
        }
        catch (RpcAuthRejectedException) { }
    }

    [TestMethod]
    public void AuthNoneReturnsAnonymous()
    {
        var buf = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(buf);
        w.WriteUInt32(RpcConstants.AuthNone);
        w.WriteOpaque([]);

        var reader = new XdrReader(buf.WrittenSpan);
        var cred = RpcAuthDecoder.ReadCredential(ref reader);
        Assert.AreEqual(0u, cred.Uid);
        Assert.AreEqual(0u, cred.Gid);
        Assert.AreEqual(string.Empty, cred.MachineName);
    }

    private static byte[] BuildAuthSysCred(uint stamp, string machineName, uint uid, uint gid, uint[] gids)
    {
        var inner = new ArrayBufferWriter<byte>();
        var iw = new XdrWriter(inner);
        iw.WriteUInt32(stamp);
        iw.WriteString(machineName);
        iw.WriteUInt32(uid);
        iw.WriteUInt32(gid);
        iw.WriteUInt32Array(gids);

        var outer = new ArrayBufferWriter<byte>();
        var ow = new XdrWriter(outer);
        ow.WriteUInt32(RpcConstants.AuthSys);
        ow.WriteOpaque(inner.WrittenSpan);
        return outer.WrittenSpan.ToArray();
    }
}
