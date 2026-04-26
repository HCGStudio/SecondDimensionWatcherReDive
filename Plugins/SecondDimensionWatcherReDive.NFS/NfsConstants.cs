namespace SecondDimensionWatcherReDive.NFS;

internal static class NfsConstants
{
    public const uint NfsProgram = 100003;
    public const uint NfsV4 = 4;
    public const int Fh4MaxSize = 128;
    public const int MaxRead = 65536;
    public const int MaxName = 255;
    public const long MaxFileSize = long.MaxValue;
    public const ulong FsIdMajor = 1;
    public const ulong FsIdMinor = 1;

    public const uint OpAccess = 3;
    public const uint OpClose = 4;
    public const uint OpCommit = 5;
    public const uint OpCreate = 6;
    public const uint OpDelegPurge = 7;
    public const uint OpDelegReturn = 8;
    public const uint OpGetAttr = 9;
    public const uint OpGetFh = 10;
    public const uint OpLink = 11;
    public const uint OpLock = 12;
    public const uint OpLockT = 13;
    public const uint OpLockU = 14;
    public const uint OpLookup = 15;
    public const uint OpLookupP = 16;
    public const uint OpNVerify = 17;
    public const uint OpOpen = 18;
    public const uint OpOpenAttr = 19;
    public const uint OpOpenConfirm = 20;
    public const uint OpOpenDowngrade = 21;
    public const uint OpPutFh = 22;
    public const uint OpPutPubFh = 23;
    public const uint OpPutRootFh = 24;
    public const uint OpRead = 25;
    public const uint OpReadDir = 26;
    public const uint OpReadLink = 27;
    public const uint OpRemove = 28;
    public const uint OpRename = 29;
    public const uint OpRenew = 30;
    public const uint OpRestoreFh = 31;
    public const uint OpSaveFh = 32;
    public const uint OpSecInfo = 33;
    public const uint OpSetAttr = 34;
    public const uint OpSetClientId = 35;
    public const uint OpSetClientIdConfirm = 36;
    public const uint OpVerify = 37;
    public const uint OpWrite = 38;
    public const uint OpReleaseLockOwner = 39;
    public const uint OpIllegal = 10044;

    public const uint Ok = 0;
    public const uint ErrPerm = 1;
    public const uint ErrNoEnt = 2;
    public const uint ErrIo = 5;
    public const uint ErrAccess = 13;
    public const uint ErrExist = 17;
    public const uint ErrNotDir = 20;
    public const uint ErrIsDir = 21;
    public const uint ErrInval = 22;
    public const uint ErrFBig = 27;
    public const uint ErrNoSpc = 28;
    public const uint ErrRoFs = 30;
    public const uint ErrNameTooLong = 63;
    public const uint ErrStale = 70;
    public const uint ErrBadHandle = 10001;
    public const uint ErrBadCookie = 10003;
    public const uint ErrNotSupp = 10004;
    public const uint ErrServerFault = 10006;
    public const uint ErrBadType = 10007;
    public const uint ErrNoFileHandle = 10020;
    public const uint ErrMinorVersMismatch = 10021;
    public const uint ErrStaleClientId = 10022;
    public const uint ErrStaleStateId = 10023;
    public const uint ErrBadStateId = 10025;
    public const uint ErrBadSeqid = 10026;
    public const uint ErrAttrNotSupp = 10032;
    public const uint ErrBadXdr = 10036;
    public const uint ErrOpenMode = 10038;
    public const uint ErrBadName = 10041;
    public const uint ErrLockNotSupp = 10043;
    public const uint ErrOpIllegal = 10044;

    public const uint Nf4Reg = 1;
    public const uint Nf4Dir = 2;
    public const uint Nf4Lnk = 5;

    public const int FattrSupportedAttrs = 0;
    public const int FattrType = 1;
    public const int FattrFhExpireType = 2;
    public const int FattrChange = 3;
    public const int FattrSize = 4;
    public const int FattrLinkSupport = 5;
    public const int FattrSymlinkSupport = 6;
    public const int FattrNamedAttr = 7;
    public const int FattrFsId = 8;
    public const int FattrUniqueHandles = 9;
    public const int FattrLeaseTime = 10;
    public const int FattrRdAttrError = 11;
    public const int FattrFilehandle = 19;
    public const int FattrFileId = 20;
    public const int FattrMaxFileSize = 27;
    public const int FattrMaxName = 29;
    public const int FattrMaxRead = 30;
    public const int FattrMode = 33;
    public const int FattrNumLinks = 35;
    public const int FattrOwner = 36;
    public const int FattrOwnerGroup = 37;
    public const int FattrSpaceUsed = 45;
    public const int FattrTimeAccess = 47;
    public const int FattrTimeMetadata = 52;
    public const int FattrTimeModify = 53;

    public const uint Fh4Persistent = 0;

    public const uint Access4Read = 0x01;
    public const uint Access4Lookup = 0x02;
    public const uint Access4Modify = 0x04;
    public const uint Access4Extend = 0x08;
    public const uint Access4Delete = 0x10;
    public const uint Access4Execute = 0x20;

    public const uint Open4ShareAccessRead = 1;
    public const uint Open4ShareAccessWrite = 2;
    public const uint Open4ShareAccessBoth = 3;

    public const uint Open4NoCreate = 0;
    public const uint Open4Create = 1;

    public const uint Open4ResultConfirm = 2;
    public const uint Open4ResultLocktypePosix = 4;
}
