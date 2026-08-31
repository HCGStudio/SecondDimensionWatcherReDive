namespace SecondDimensionWatcherReDive.NFS.Rpc;

internal static class RpcConstants
{
    public const uint RpcVersion = 2;

    public const uint Call = 0;
    public const uint Reply = 1;

    public const uint MsgAccepted = 0;
    public const uint MsgDenied = 1;

    public const uint Success = 0;
    public const uint ProgUnavail = 1;
    public const uint ProgMismatch = 2;
    public const uint ProcUnavail = 3;
    public const uint GarbageArgs = 4;
    public const uint SystemErr = 5;

    public const uint RpcMismatch = 0;
    public const uint AuthError = 1;

    public const uint AuthBadCred = 1;
    public const uint AuthRejectedCred = 2;
    public const uint AuthBadVerf = 3;
    public const uint AuthRejectedVerf = 4;
    public const uint AuthTooWeak = 5;
    public const uint AuthInvalidResp = 6;
    public const uint AuthFailed = 7;

    public const uint AuthNone = 0;
    public const uint AuthSys = 1;
    public const uint AuthShort = 2;
    public const uint RpcSecGss = 6;

    public const uint NfsProcNull = 0;
    public const uint NfsProcCompound = 1;

    public const int RecordHeaderSize = 4;
    public const uint LastFragmentMask = 0x80000000u;
    public const uint LengthMask = 0x7FFFFFFFu;

    public const int MaxRequestBytes = 1 * 1024 * 1024;
    public const int MaxFragmentsPerRecord = 1024;
    public const int MaxMachineNameBytes = 255;
    public const int MaxOpaqueAuthBytes = 400;
}
