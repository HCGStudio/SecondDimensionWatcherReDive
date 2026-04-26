using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.NFS.Protocol;

internal sealed record NfsCompoundRequest(
    string Tag,
    uint MinorVersion,
    IReadOnlyList<NfsOperation> Operations);

internal static class NfsCompoundDecoder
{
    public static NfsCompoundRequest Decode(ReadOnlySpan<byte> body)
    {
        var reader = new XdrReader(body);
        var tag = reader.ReadString();
        var minorVersion = reader.ReadUInt32();
        var argCount = reader.ReadUInt32();
        if (argCount > 256)
            throw new XdrException($"Implausible op count {argCount}");

        var ops = new List<NfsOperation>((int)argCount);
        for (var i = 0; i < argCount; i++)
            ops.Add(DecodeOp(ref reader));
        return new NfsCompoundRequest(tag, minorVersion, ops);
    }

    private static NfsOperation DecodeOp(ref XdrReader reader)
    {
        var opCode = reader.ReadUInt32();
        switch (opCode)
        {
            case NfsConstants.OpPutRootFh:
                return new PutRootFhOp();

            case NfsConstants.OpPutFh:
                return new PutFhOp(reader.ReadOpaque().ToArray());

            case NfsConstants.OpGetFh:
                return new GetFhOp();

            case NfsConstants.OpSaveFh:
                return new SaveFhOp();

            case NfsConstants.OpRestoreFh:
                return new RestoreFhOp();

            case NfsConstants.OpLookup:
                return new LookupOp(reader.ReadString());

            case NfsConstants.OpLookupP:
                return new LookupPOp();

            case NfsConstants.OpGetAttr:
                return new GetAttrOp(reader.ReadUInt32Array());

            case NfsConstants.OpAccess:
                return new AccessOp(reader.ReadUInt32());

            case NfsConstants.OpReadDir:
            {
                var cookie = reader.ReadUInt64();
                var verifier = reader.ReadUInt64();
                var dirCount = reader.ReadUInt32();
                var maxCount = reader.ReadUInt32();
                var attrs = reader.ReadUInt32Array();
                return new ReadDirOp(cookie, verifier, dirCount, maxCount, attrs);
            }

            case NfsConstants.OpRead:
            {
                var stateId = NfsStateId.Read(ref reader);
                var offset = reader.ReadUInt64();
                var count = reader.ReadUInt32();
                return new ReadOp(stateId, offset, count);
            }

            case NfsConstants.OpOpen:
                return DecodeOpen(ref reader);

            case NfsConstants.OpOpenConfirm:
            {
                var stateId = NfsStateId.Read(ref reader);
                var seqId = reader.ReadUInt32();
                return new OpenConfirmOp(stateId, seqId);
            }

            case NfsConstants.OpClose:
            {
                var seqId = reader.ReadUInt32();
                var stateId = NfsStateId.Read(ref reader);
                return new CloseOp(seqId, stateId);
            }

            case NfsConstants.OpSetClientId:
            {
                var verifier = reader.ReadUInt64();
                var clientIdBytes = reader.ReadOpaque().ToArray();
                var cbProgram = reader.ReadUInt32();
                var cbNetId = reader.ReadString();
                var cbAddress = reader.ReadString();
                var cbIdent = reader.ReadUInt32();
                return new SetClientIdOp(verifier, clientIdBytes, cbProgram, cbNetId, cbAddress, cbIdent);
            }

            case NfsConstants.OpSetClientIdConfirm:
            {
                var clientId = reader.ReadUInt64();
                var verifier = reader.ReadUInt64();
                return new SetClientIdConfirmOp(clientId, verifier);
            }

            case NfsConstants.OpRenew:
                return new RenewOp(reader.ReadUInt64());

            case NfsConstants.OpSecInfo:
                return new SecInfoOp(reader.ReadString());

            case NfsConstants.OpDelegReturn:
                return new DelegReturnOp(NfsStateId.Read(ref reader));

            case NfsConstants.OpReleaseLockOwner:
            {
                var clientId = reader.ReadUInt64();
                var owner = reader.ReadOpaque().ToArray();
                return new ReleaseLockOwnerOp(clientId, owner);
            }

            case NfsConstants.OpWrite:
            {
                _ = NfsStateId.Read(ref reader);
                _ = reader.ReadUInt64();
                _ = reader.ReadUInt32();
                _ = reader.ReadOpaque();
                return new UnsupportedOp(NfsConstants.OpWrite, NfsConstants.ErrRoFs);
            }
            case NfsConstants.OpRemove:
            {
                _ = reader.ReadString();
                return new UnsupportedOp(NfsConstants.OpRemove, NfsConstants.ErrRoFs);
            }
            case NfsConstants.OpRename:
            {
                _ = reader.ReadString();
                _ = reader.ReadString();
                return new UnsupportedOp(NfsConstants.OpRename, NfsConstants.ErrRoFs);
            }
            case NfsConstants.OpLink:
            {
                _ = reader.ReadString();
                return new UnsupportedOp(NfsConstants.OpLink, NfsConstants.ErrRoFs);
            }
            case NfsConstants.OpCommit:
            {
                _ = reader.ReadUInt64();
                _ = reader.ReadUInt32();
                return new UnsupportedOp(NfsConstants.OpCommit, NfsConstants.ErrRoFs);
            }
            case NfsConstants.OpSetAttr:
            {
                _ = NfsStateId.Read(ref reader);
                _ = reader.ReadUInt32Array();
                _ = reader.ReadOpaque();
                return new UnsupportedOp(NfsConstants.OpSetAttr, NfsConstants.ErrRoFs);
            }

            default:
                throw new XdrException($"Unsupported NFS operation 0x{opCode:X4}");
        }
    }

    private static OpenOp DecodeOpen(ref XdrReader reader)
    {
        var seqId = reader.ReadUInt32();
        var shareAccess = reader.ReadUInt32();
        var shareDeny = reader.ReadUInt32();
        var clientId = reader.ReadUInt64();
        var owner = reader.ReadOpaque().ToArray();
        var openType = reader.ReadUInt32();
        if (openType == NfsConstants.Open4Create)
        {
            var mode = reader.ReadUInt32();
            switch (mode)
            {
                case 0:
                case 1:
                    _ = reader.ReadUInt32Array();
                    _ = reader.ReadOpaque();
                    break;
                case 2:
                    _ = reader.ReadUInt64();
                    break;
                default:
                    throw new XdrException($"Unknown OPEN createmode {mode}");
            }
        }

        var claimType = reader.ReadUInt32();
        var fileName = string.Empty;
        switch (claimType)
        {
            case 0:
                fileName = reader.ReadString();
                break;
            case 1:
                _ = reader.ReadUInt32();
                break;
            case 2:
                _ = NfsStateId.Read(ref reader);
                fileName = reader.ReadString();
                break;
            case 3:
                fileName = reader.ReadString();
                break;
            default:
                throw new XdrException($"Unknown OPEN claim type {claimType}");
        }

        return new OpenOp(seqId, shareAccess, shareDeny, clientId, owner, openType, claimType, fileName);
    }
}
