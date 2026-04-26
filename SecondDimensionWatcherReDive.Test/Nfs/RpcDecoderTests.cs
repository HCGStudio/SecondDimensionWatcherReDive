using System.Buffers;
using SecondDimensionWatcherReDive.NFS.Auth;
using SecondDimensionWatcherReDive.NFS.Rpc;
using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.Test.Nfs;

[TestClass]
public class RpcDecoderTests
{
    [TestMethod]
    public void DecodeCall_AuthNoneVerifierNone()
    {
        var bytes = BuildCall(
            xid: 0xDEADBEEF,
            program: 100003,
            version: 4,
            procedure: 1,
            credential: CredentialAuthNone(),
            verifier: VerifierAuthNone(),
            body: [0xCA, 0xFE]);

        var (header, bodyOffset) = RpcDecoder.DecodeCall(bytes);
        Assert.AreEqual(0xDEADBEEFu, header.Xid);
        Assert.AreEqual(100003u, header.Program);
        Assert.AreEqual(4u, header.Version);
        Assert.AreEqual(1u, header.Procedure);
        Assert.AreEqual(0u, header.Credential.Uid);
        Assert.AreEqual(string.Empty, header.Credential.MachineName);
        CollectionAssert.AreEqual(new byte[] { 0xCA, 0xFE }, bytes[bodyOffset..]);
    }

    [TestMethod]
    public void DecodeCall_AuthSysParsesCredential()
    {
        var bytes = BuildCall(
            xid: 1,
            program: 100003,
            version: 4,
            procedure: 1,
            credential: CredentialAuthSys(0xAABB, "client.example", uid: 1000, gid: 1000, gids: [1001, 1002]),
            verifier: VerifierAuthNone(),
            body: []);

        var (header, _) = RpcDecoder.DecodeCall(bytes);
        var cred = header.Credential;
        Assert.AreEqual(0xAABBu, cred.Stamp);
        Assert.AreEqual("client.example", cred.MachineName);
        Assert.AreEqual(1000u, cred.Uid);
        Assert.AreEqual(1000u, cred.Gid);
        CollectionAssert.AreEqual(new uint[] { 1001, 1002 }, cred.Gids);
    }

    [TestMethod]
    public void DecodeCall_RejectsRpcSecGss()
    {
        var bytes = BuildCall(
            xid: 1,
            program: 100003,
            version: 4,
            procedure: 1,
            credential: CredentialRpcSecGss(),
            verifier: VerifierAuthNone(),
            body: []);

        Assert.ThrowsExactly<RpcAuthRejectedException>(() => RpcDecoder.DecodeCall(bytes));
    }

    [TestMethod]
    public void DecodeCall_WrongMessageTypeThrows()
    {
        var bytes = BuildCall(
            xid: 1,
            program: 100003,
            version: 4,
            procedure: 1,
            credential: CredentialAuthNone(),
            verifier: VerifierAuthNone(),
            body: [],
            messageType: RpcConstants.Reply);

        Assert.ThrowsExactly<RpcMalformedException>(() => RpcDecoder.DecodeCall(bytes));
    }

    private static byte[] CredentialAuthNone()
    {
        var buf = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(buf);
        w.WriteUInt32(RpcConstants.AuthNone);
        w.WriteOpaque([]);
        return buf.WrittenSpan.ToArray();
    }

    private static byte[] CredentialAuthSys(uint stamp, string machineName, uint uid, uint gid, uint[] gids)
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

    private static byte[] CredentialRpcSecGss()
    {
        var buf = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(buf);
        w.WriteUInt32(RpcConstants.RpcSecGss);
        w.WriteOpaque([0xCA, 0xFE]);
        return buf.WrittenSpan.ToArray();
    }

    private static byte[] VerifierAuthNone()
    {
        var buf = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(buf);
        w.WriteUInt32(RpcConstants.AuthNone);
        w.WriteOpaque([]);
        return buf.WrittenSpan.ToArray();
    }

    private static byte[] BuildCall(
        uint xid,
        uint program,
        uint version,
        uint procedure,
        byte[] credential,
        byte[] verifier,
        byte[] body,
        uint messageType = RpcConstants.Call)
    {
        var buf = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(buf);
        w.WriteUInt32(xid);
        w.WriteUInt32(messageType);
        w.WriteUInt32(RpcConstants.RpcVersion);
        w.WriteUInt32(program);
        w.WriteUInt32(version);
        w.WriteUInt32(procedure);
        w.WriteRaw(credential);
        w.WriteRaw(verifier);
        w.WriteRaw(body);
        return buf.WrittenSpan.ToArray();
    }
}
