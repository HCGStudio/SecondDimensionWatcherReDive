namespace SecondDimensionWatcherReDive.NFS.Protocol;

internal abstract record NfsOperation(uint OpCode);

internal sealed record PutRootFhOp() : NfsOperation(NfsConstants.OpPutRootFh);

internal sealed record PutFhOp(byte[] Handle) : NfsOperation(NfsConstants.OpPutFh);

internal sealed record GetFhOp() : NfsOperation(NfsConstants.OpGetFh);

internal sealed record SaveFhOp() : NfsOperation(NfsConstants.OpSaveFh);

internal sealed record RestoreFhOp() : NfsOperation(NfsConstants.OpRestoreFh);

internal sealed record LookupOp(string Name) : NfsOperation(NfsConstants.OpLookup);

internal sealed record LookupPOp() : NfsOperation(NfsConstants.OpLookupP);

internal sealed record GetAttrOp(uint[] AttrRequest) : NfsOperation(NfsConstants.OpGetAttr);

internal sealed record AccessOp(uint Mask) : NfsOperation(NfsConstants.OpAccess);

internal sealed record ReadDirOp(
    ulong Cookie,
    ulong Verifier,
    uint DirCount,
    uint MaxCount,
    uint[] AttrRequest) : NfsOperation(NfsConstants.OpReadDir);

internal sealed record ReadOp(NfsStateId StateId, ulong Offset, uint Count)
    : NfsOperation(NfsConstants.OpRead);

internal sealed record OpenOp(
    uint SeqId,
    uint ShareAccess,
    uint ShareDeny,
    ulong ClientId,
    byte[] Owner,
    uint OpenType,
    uint ClaimType,
    string FileName) : NfsOperation(NfsConstants.OpOpen);

internal sealed record OpenConfirmOp(NfsStateId StateId, uint SeqId)
    : NfsOperation(NfsConstants.OpOpenConfirm);

internal sealed record CloseOp(uint SeqId, NfsStateId StateId)
    : NfsOperation(NfsConstants.OpClose);

internal sealed record SetClientIdOp(
    ulong Verifier,
    byte[] ClientIdBytes,
    uint CallbackProgram,
    string CallbackNetId,
    string CallbackAddress,
    uint CallbackIdent) : NfsOperation(NfsConstants.OpSetClientId);

internal sealed record SetClientIdConfirmOp(ulong ClientId, ulong Verifier)
    : NfsOperation(NfsConstants.OpSetClientIdConfirm);

internal sealed record RenewOp(ulong ClientId) : NfsOperation(NfsConstants.OpRenew);

internal sealed record SecInfoOp(string Name) : NfsOperation(NfsConstants.OpSecInfo);

internal sealed record DelegReturnOp(NfsStateId StateId)
    : NfsOperation(NfsConstants.OpDelegReturn);

internal sealed record ReleaseLockOwnerOp(ulong ClientId, byte[] Owner)
    : NfsOperation(NfsConstants.OpReleaseLockOwner);

internal sealed record UnsupportedOp(uint ResolvedOpCode, uint MappedStatus)
    : NfsOperation(ResolvedOpCode);
