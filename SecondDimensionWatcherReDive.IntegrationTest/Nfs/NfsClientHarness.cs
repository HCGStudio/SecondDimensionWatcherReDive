using System.Buffers;
using System.Net.Sockets;
using SecondDimensionWatcherReDive.NFS;
using SecondDimensionWatcherReDive.NFS.Auth;
using SecondDimensionWatcherReDive.NFS.Protocol;
using SecondDimensionWatcherReDive.NFS.Rpc;
using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.IntegrationTest.Nfs;

/// Minimal NFSv4 client used by integration tests. Builds COMPOUND requests
/// using the production XDR/RPC writers, sends over a real TCP socket, and
/// returns the raw reply bytes (post-RPC-header) for the test to parse.
internal sealed class NfsClientHarness : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private uint _xidCounter;

    private NfsClientHarness(TcpClient client, NetworkStream stream)
    {
        _client = client;
        _stream = stream;
    }

    public static async Task<NfsClientHarness> ConnectAsync(int port, CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, port, cancellationToken);
        return new NfsClientHarness(client, client.GetStream());
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
        _client.Dispose();
    }

    public async Task<NfsRawReply> CompoundAsync(
        string tag,
        IEnumerable<Action<XdrWriter>> opWriters,
        CancellationToken cancellationToken)
    {
        var xid = ++_xidCounter;
        var body = new ArrayBufferWriter<byte>();
        var bodyWriter = new XdrWriter(body);
        bodyWriter.WriteString(tag);
        bodyWriter.WriteUInt32(0);
        var ops = opWriters.ToArray();
        bodyWriter.WriteUInt32((uint)ops.Length);
        foreach (var op in ops)
            op(bodyWriter);

        var call = BuildCallMessage(xid, RpcConstants.NfsProcCompound, body.WrittenSpan);
        await RpcRecordReader.WriteAsync(_stream, call, cancellationToken);

        using var record = await RpcRecordReader.ReadAsync(
            _stream, RpcConstants.MaxRequestBytes, cancellationToken)
            ?? throw new InvalidOperationException("server closed connection");

        return ParseReply(record.Memory.ToArray(), xid, hasCompoundBody: true);
    }

    public async Task<NfsRawReply> NullAsync(CancellationToken cancellationToken)
    {
        var xid = ++_xidCounter;
        var call = BuildCallMessage(xid, RpcConstants.NfsProcNull, [], useAuthNone: true);
        await RpcRecordReader.WriteAsync(_stream, call, cancellationToken);

        using var record = await RpcRecordReader.ReadAsync(
            _stream, RpcConstants.MaxRequestBytes, cancellationToken)
            ?? throw new InvalidOperationException("server closed connection");

        return ParseReply(record.Memory.ToArray(), xid, hasCompoundBody: false);
    }

    private static byte[] BuildCallMessage(
        uint xid,
        uint procedure,
        ReadOnlySpan<byte> body,
        bool useAuthNone = false)
    {
        var call = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(call);
        w.WriteUInt32(xid);
        w.WriteUInt32(RpcConstants.Call);
        w.WriteUInt32(RpcConstants.RpcVersion);
        w.WriteUInt32(NfsConstants.NfsProgram);
        w.WriteUInt32(NfsConstants.NfsV4);
        w.WriteUInt32(procedure);
        if (useAuthNone)
        {
            w.WriteUInt32(RpcConstants.AuthNone);
            w.WriteOpaque([]);
        }
        else
        {
            WriteAuthSysCred(w, "harness", uid: 1000, gid: 1000);
        }
        w.WriteUInt32(RpcConstants.AuthNone);
        w.WriteOpaque([]);
        w.WriteRaw(body);
        return call.WrittenSpan.ToArray();
    }

    private static void WriteAuthSysCred(XdrWriter writer, string machineName, uint uid, uint gid)
    {
        var inner = new ArrayBufferWriter<byte>();
        var iw = new XdrWriter(inner);
        iw.WriteUInt32(0);
        iw.WriteString(machineName);
        iw.WriteUInt32(uid);
        iw.WriteUInt32(gid);
        iw.WriteUInt32Array(ReadOnlySpan<uint>.Empty);
        writer.WriteUInt32(RpcConstants.AuthSys);
        writer.WriteOpaque(inner.WrittenSpan);
    }

    private static NfsRawReply ParseReply(byte[] reply, uint expectedXid, bool hasCompoundBody)
    {
        var reader = new XdrReader(reply);
        var xid = reader.ReadUInt32();
        if (xid != expectedXid)
            throw new InvalidOperationException($"xid mismatch: expected {expectedXid}, got {xid}");
        var mtype = reader.ReadUInt32();
        if (mtype != RpcConstants.Reply)
            throw new InvalidOperationException("not a REPLY");
        var stat = reader.ReadUInt32();
        if (stat != RpcConstants.MsgAccepted)
        {
            return new NfsRawReply(false, RpcConstants.MsgDenied, 0, string.Empty, []);
        }
        _ = reader.ReadUInt32();
        _ = reader.ReadOpaque();
        var acceptStat = reader.ReadUInt32();
        if (acceptStat != RpcConstants.Success || !hasCompoundBody)
            return new NfsRawReply(true, acceptStat, 0, string.Empty, []);

        var compoundStatus = reader.ReadUInt32();
        var tag = reader.ReadString();
        var bodyOffset = reader.Consumed;
        return new NfsRawReply(true, acceptStat, compoundStatus, tag, reply.AsMemory(bodyOffset).ToArray());
    }

    public static Action<XdrWriter> Op(uint opCode, Action<XdrWriter>? args = null) => w =>
    {
        w.WriteUInt32(opCode);
        args?.Invoke(w);
    };

    public static Action<XdrWriter> OpPutRootFh() => Op(NfsConstants.OpPutRootFh);

    public static Action<XdrWriter> OpPutFh(byte[] handle) =>
        Op(NfsConstants.OpPutFh, w => w.WriteOpaque(handle));

    public static Action<XdrWriter> OpGetFh() => Op(NfsConstants.OpGetFh);

    public static Action<XdrWriter> OpLookup(string name) =>
        Op(NfsConstants.OpLookup, w => w.WriteString(name));

    public static Action<XdrWriter> OpGetAttr(uint[] bitmap) =>
        Op(NfsConstants.OpGetAttr, w => w.WriteUInt32Array(bitmap));

    public static Action<XdrWriter> OpReadDir(ulong cookie, uint dircount, uint maxcount, uint[] bitmap) =>
        Op(NfsConstants.OpReadDir, w =>
        {
            w.WriteUInt64(cookie);
            w.WriteUInt64(0);
            w.WriteUInt32(dircount);
            w.WriteUInt32(maxcount);
            w.WriteUInt32Array(bitmap);
        });

    public static Action<XdrWriter> OpRead(NfsStateId state, ulong offset, uint count) =>
        Op(NfsConstants.OpRead, w =>
        {
            state.WriteTo(w);
            w.WriteUInt64(offset);
            w.WriteUInt32(count);
        });

    public static Action<XdrWriter> OpWrite(NfsStateId state, ulong offset, byte[] data) =>
        Op(NfsConstants.OpWrite, w =>
        {
            state.WriteTo(w);
            w.WriteUInt64(offset);
            w.WriteUInt32(0);
            w.WriteOpaque(data);
        });
}

/// Raw reply from the harness: AcceptStatus is the RPC accept_stat (Success
/// for normal NFS COMPOUND replies). CompoundStatus is the COMPOUND-level
/// nfsstat4. ResArrayBytes contains the count + per-op bytes after the
/// `tag` field, which the test parses with an XdrReader directly.
internal sealed record NfsRawReply(
    bool MessageAccepted,
    uint AcceptStatus,
    uint CompoundStatus,
    string Tag,
    byte[] ResArrayBytes);
