using SecondDimensionWatcherReDive.NFS.Auth;
using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.NFS.Rpc;

internal sealed record RpcCallHeader(
    uint Xid,
    uint Program,
    uint Version,
    uint Procedure,
    AuthSysCred Credential);

internal sealed class RpcMalformedException : Exception
{
    public RpcMalformedException(string message) : base(message) { }
    public RpcMalformedException(string message, Exception inner) : base(message, inner) { }
}

internal sealed class RpcProgramMismatchException : Exception
{
    public uint ExpectedProgram { get; }
    public uint ExpectedVersion { get; }

    public RpcProgramMismatchException(uint expectedProgram, uint expectedVersion, string message)
        : base(message)
    {
        ExpectedProgram = expectedProgram;
        ExpectedVersion = expectedVersion;
    }
}

internal static class RpcDecoder
{
    public static (RpcCallHeader Header, int BodyOffset) DecodeCall(
        ReadOnlySpan<byte> message,
        bool allowAnonymous = false)
    {
        var reader = new XdrReader(message);
        try
        {
            var xid = reader.ReadUInt32();
            var mtype = reader.ReadUInt32();
            if (mtype != RpcConstants.Call)
                throw new RpcMalformedException($"Expected CALL message, got mtype {mtype}");
            var rpcvers = reader.ReadUInt32();
            if (rpcvers != RpcConstants.RpcVersion)
                throw new RpcMalformedException($"Unsupported RPC version {rpcvers}");
            var program = reader.ReadUInt32();
            var version = reader.ReadUInt32();
            var procedure = reader.ReadUInt32();
            // RPC NULL has no body and grants no resource access. Standard rpcinfo and
            // mount probes commonly send it with AUTH_NONE even when COMPOUND requires sec=sys.
            var cred = RpcAuthDecoder.ReadCredential(
                ref reader,
                allowAnonymous || procedure == RpcConstants.NfsProcNull);
            RpcAuthDecoder.ReadAndDiscardVerifier(ref reader);

            var header = new RpcCallHeader(xid, program, version, procedure, cred);
            return (header, reader.Consumed);
        }
        catch (XdrException ex)
        {
            throw new RpcMalformedException("Malformed RPC call header", ex);
        }
    }
}
