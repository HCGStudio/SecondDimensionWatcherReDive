using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.NFS;
using SecondDimensionWatcherReDive.NFS.Auth;
using SecondDimensionWatcherReDive.NFS.Protocol;
using SecondDimensionWatcherReDive.NFS.Server;
using SecondDimensionWatcherReDive.NFS.Vfs;
using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.Test.Nfs;

[TestClass]
public class NfsCompoundDispatcherTests
{
    private static readonly DateTimeOffset s_modified = new(2026, 4, 27, 0, 0, 0, TimeSpan.Zero);
    private static readonly byte[] s_fileBytes = "hello-world-this-is-a-test-payload"u8.ToArray();
    private static readonly FileMapping s_mapping = new(
        Guid.NewGuid(), Guid.NewGuid(), "/anime-a/01.mkv", "/disk/01.mkv", "local");

    private Mock<IFileExplorer> _explorer = null!;
    private Mock<IFileMappingRepository> _mappingRepo = null!;
    private Mock<IFileStoreProvider> _storeProvider = null!;
    private Mock<IFileStore> _store = null!;
    private NfsCompoundDispatcher _dispatcher = null!;
    private NfsOpenStateRegistry _opens = null!;
    private NfsClientRegistry _clients = null!;

    [TestInitialize]
    public void Setup()
    {
        _explorer = new Mock<IFileExplorer>();
        _mappingRepo = new Mock<IFileMappingRepository>();
        _storeProvider = new Mock<IFileStoreProvider>();
        _store = new Mock<IFileStore>();

        _explorer.Setup(e => e.GetDirectoryEntriesAsync(
                It.Is<DirectoryToken>(t => t.Path == "/"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<FileExploreEntry>)
                [new FileExploreEntry("/anime-a", "anime-a", true, null, null)]);

        _explorer.Setup(e => e.GetDirectoryEntriesAsync(
                It.Is<DirectoryToken>(t => t.Path == "/anime-a"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<FileExploreEntry>)
            [
                new FileExploreEntry(
                    "/anime-a/01.mkv",
                    "01.mkv",
                    false,
                    s_mapping,
                    new FileStoreInfo(false, "/disk/01.mkv", "01.mkv", s_fileBytes.Length, s_modified)),
                new FileExploreEntry("/anime-a/sub", "sub", true, null, null),
            ]);

        _mappingRepo.Setup(r => r.FindFileSystemEntryAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, CancellationToken _) => path switch
            {
                "/anime-a" => new FileSystemEntry("/anime-a", "/", "anime-a", true, null),
                "/anime-a/01.mkv" => new FileSystemEntry(
                    "/anime-a/01.mkv", "/anime-a", "01.mkv", false, s_mapping),
                "/anime-a/sub" => new FileSystemEntry("/anime-a/sub", "/anime-a", "sub", true, null),
                _ => null
            });

        _storeProvider.Setup(p => p.GetClient("local")).Returns(_store.Object);

        _store.Setup(s => s.FileInfoAsync("/disk/01.mkv", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileStoreInfo(false, "/disk/01.mkv", "01.mkv", s_fileBytes.Length, s_modified));

        _explorer.Setup(e => e.OpenReadStreamAsync(
                It.Is<FileToken>(t => t.Path == "/anime-a/01.mkv"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(s_fileBytes, writable: false));

        var vfs = new NfsVfsAdapter(_explorer.Object, _mappingRepo.Object, _storeProvider.Object);
        _opens = new NfsOpenStateRegistry();
        _clients = new NfsClientRegistry();
        _dispatcher = new NfsCompoundDispatcher(
            vfs, _clients, _opens,
            Options.Create(new NfsOptions { LeaseSeconds = 90 }),
            NullLogger<NfsCompoundDispatcher>.Instance);
    }

    [TestMethod]
    public async Task PutRootFh_GetFh_ReturnsRootHandle()
    {
        var ctx = new NfsRequestContext { Credential = AuthSysCred.Anonymous };
        var result = await _dispatcher.DispatchAsync(
            new NfsCompoundRequest("", 0, [new PutRootFhOp(), new GetFhOp()]), ctx);

        Assert.AreEqual(NfsConstants.Ok, result.Status);
        Assert.AreEqual(2, result.Results.Count);

        var getFhBody = result.Results[1].Body;
        var reader = new XdrReader(getFhBody);
        Assert.AreEqual(NfsConstants.Ok, reader.ReadUInt32());
        var handleBytes = reader.ReadOpaque().ToArray();
        var handle = NfsFileHandle.FromBytes(handleBytes);
        Assert.AreSame(NfsFileHandle.Root, handle);
    }

    [TestMethod]
    public async Task GetFh_WithoutCfh_ReturnsNoFileHandle()
    {
        var ctx = new NfsRequestContext();
        var result = await DispatchAsync(ctx, new GetFhOp());
        Assert.AreEqual(NfsConstants.ErrNoFileHandle, result.Status);
    }

    [TestMethod]
    public async Task PutFh_BadBytes_ReturnsBadHandle()
    {
        var ctx = new NfsRequestContext();
        var result = await DispatchAsync(ctx, new PutFhOp([0, 0, 0]));
        Assert.AreEqual(NfsConstants.ErrBadHandle, result.Status);
    }

    [TestMethod]
    public async Task Lookup_ExistingDir_AdvancesCfh()
    {
        var ctx = new NfsRequestContext();
        var result = await _dispatcher.DispatchAsync(
            new NfsCompoundRequest("", 0, [new PutRootFhOp(), new LookupOp("anime-a"), new GetFhOp()]),
            ctx);

        Assert.AreEqual(NfsConstants.Ok, result.Status);
        var getFhReader = new XdrReader(result.Results[2].Body);
        Assert.AreEqual(NfsConstants.Ok, getFhReader.ReadUInt32());
        var handle = NfsFileHandle.FromBytes(getFhReader.ReadOpaque());
        Assert.AreEqual(NfsHandleKind.Directory, handle.Kind);
        Assert.AreEqual("/anime-a", handle.VirtualPath);
    }

    [TestMethod]
    public async Task Lookup_MissingName_ReturnsNoEnt()
    {
        var ctx = new NfsRequestContext();
        var result = await _dispatcher.DispatchAsync(
            new NfsCompoundRequest("", 0, [new PutRootFhOp(), new LookupOp("does-not-exist")]),
            ctx);
        Assert.AreEqual(NfsConstants.ErrNoEnt, result.Status);
    }

    [TestMethod]
    public async Task LookupP_FromRoot_ReturnsNoEnt()
    {
        var ctx = new NfsRequestContext();
        var result = await _dispatcher.DispatchAsync(
            new NfsCompoundRequest("", 0, [new PutRootFhOp(), new LookupPOp()]), ctx);
        Assert.AreEqual(NfsConstants.ErrNoEnt, result.Status);
    }

    [TestMethod]
    public async Task LookupP_FromAnimeDir_ReachesRoot()
    {
        var ctx = new NfsRequestContext { CurrentFh = new NfsFileHandle(NfsHandleKind.Directory, "/anime-a") };
        var result = await DispatchAsync(ctx, new LookupPOp());
        Assert.AreEqual(NfsConstants.Ok, result.Status);
        Assert.AreEqual(NfsHandleKind.Root, ctx.CurrentFh!.Kind);
    }

    [TestMethod]
    public async Task GetAttr_ForFile_EncodesTypeAndSize()
    {
        var ctx = new NfsRequestContext { CurrentFh = new NfsFileHandle(NfsHandleKind.File, "/anime-a/01.mkv") };
        var bitmap = NfsAttributes.BitmapFromIds([NfsConstants.FattrType, NfsConstants.FattrSize]);
        var result = await DispatchAsync(ctx, new GetAttrOp(bitmap));

        Assert.AreEqual(NfsConstants.Ok, result.Status);
        var r = new XdrReader(result.Body);
        var responseBitmap = r.ReadUInt32Array();
        var inner = new XdrReader(r.ReadOpaque());
        var ids = NfsAttributes.IdsFromBitmap(responseBitmap);
        CollectionAssert.AreEquivalent(
            new[] { NfsConstants.FattrType, NfsConstants.FattrSize }, ids);
        Assert.AreEqual(NfsConstants.Nf4Reg, inner.ReadUInt32());
        Assert.AreEqual((ulong)s_fileBytes.Length, inner.ReadUInt64());
    }

    [TestMethod]
    public async Task ReadDir_ListsChildrenSorted()
    {
        var ctx = new NfsRequestContext();
        var bitmap = NfsAttributes.BitmapFromIds([NfsConstants.FattrType]);
        var lookup = new LookupOp("anime-a");
        var readDir = new ReadDirOp(0, 0, 4096, 65536, bitmap);
        var result = await _dispatcher.DispatchAsync(
            new NfsCompoundRequest("", 0, [new PutRootFhOp(), lookup, readDir]), ctx);

        Assert.AreEqual(NfsConstants.Ok, result.Status);
        var r = new XdrReader(result.Results[2].Body);
        Assert.AreEqual(NfsConstants.Ok, r.ReadUInt32());
        var verifier = r.ReadFixedOpaque(8);
        Assert.AreEqual(8, verifier.Length);

        var names = new List<string>();
        while (r.ReadBool())
        {
            _ = r.ReadUInt64();
            names.Add(r.ReadString());
            _ = r.ReadUInt32Array();
            _ = r.ReadOpaque();
        }
        var eof = r.ReadBool();

        CollectionAssert.AreEqual(new[] { "01.mkv", "sub" }, names);
        Assert.IsTrue(eof);
    }

    [TestMethod]
    public async Task Read_ReturnsBytesAtOffset()
    {
        var ctx = new NfsRequestContext { CurrentFh = new NfsFileHandle(NfsHandleKind.File, "/anime-a/01.mkv") };
        var result = await DispatchAsync(ctx, new ReadOp(NfsStateId.AnyState, 7, 5));

        Assert.AreEqual(NfsConstants.Ok, result.Status);
        var r = new XdrReader(result.Body);
        var eof = r.ReadBool();
        var data = r.ReadOpaque().ToArray();

        CollectionAssert.AreEqual(s_fileBytes[7..12], data);
        Assert.IsFalse(eof);
    }

    [TestMethod]
    public async Task Read_PastEnd_SetsEof()
    {
        var ctx = new NfsRequestContext { CurrentFh = new NfsFileHandle(NfsHandleKind.File, "/anime-a/01.mkv") };
        var result = await DispatchAsync(ctx, new ReadOp(NfsStateId.AnyState, 0, 1024));

        Assert.AreEqual(NfsConstants.Ok, result.Status);
        var r = new XdrReader(result.Body);
        var eof = r.ReadBool();
        var data = r.ReadOpaque().ToArray();
        CollectionAssert.AreEqual(s_fileBytes, data);
        Assert.IsTrue(eof);
    }

    [TestMethod]
    public async Task OpenConfirmCloseFlow_RoundTrips()
    {
        var ctx = new NfsRequestContext();
        var open = new OpenOp(
            SeqId: 0,
            ShareAccess: NfsConstants.Open4ShareAccessRead,
            ShareDeny: 0,
            ClientId: 1,
            Owner: [1],
            OpenType: NfsConstants.Open4NoCreate,
            ClaimType: 0,
            FileName: "01.mkv");

        var compound = new NfsCompoundRequest(
            "", 0,
            [
                new PutRootFhOp(),
                new LookupOp("anime-a"),
                open,
            ]);

        var result = await _dispatcher.DispatchAsync(compound, ctx);
        Assert.AreEqual(NfsConstants.Ok, result.Status);

        var openReader = new XdrReader(result.Results[2].Body);
        Assert.AreEqual(NfsConstants.Ok, openReader.ReadUInt32());
        var stateId = NfsStateId.Read(ref openReader);
        Assert.IsFalse(stateId.IsAny);

        var beforeConfirm = _opens.Count;
        Assert.AreEqual(1, beforeConfirm);

        var confirm = await DispatchAsync(new NfsRequestContext(), new OpenConfirmOp(stateId, 1));
        Assert.AreEqual(NfsConstants.Ok, confirm.Status);

        var close = await DispatchAsync(new NfsRequestContext(), new CloseOp(2, stateId));
        Assert.AreEqual(NfsConstants.Ok, close.Status);
        Assert.AreEqual(0, _opens.Count);
    }

    [TestMethod]
    public async Task Open_WithWriteAccess_ReturnsOpenMode()
    {
        var ctx = new NfsRequestContext();
        var open = new OpenOp(
            0, NfsConstants.Open4ShareAccessBoth, 0, 1, [1],
            NfsConstants.Open4NoCreate, 0, "01.mkv");
        var result = await _dispatcher.DispatchAsync(
            new NfsCompoundRequest("", 0, [new PutRootFhOp(), new LookupOp("anime-a"), open]),
            ctx);
        Assert.AreEqual(NfsConstants.ErrOpenMode, result.Status);
    }

    [TestMethod]
    public async Task SetClientId_Confirm_RenewSucceeds()
    {
        var ctx = new NfsRequestContext();
        var set = new SetClientIdOp(0xCAFEul, [1, 2, 3], 0, "tcp", "127.0.0.1.0.0", 0);

        var setRes = await DispatchAsync(ctx, set);
        Assert.AreEqual(NfsConstants.Ok, setRes.Status);
        var setReader = new XdrReader(setRes.Body);
        var clientId = setReader.ReadUInt64();

        var confirmRes = await DispatchAsync(ctx, new SetClientIdConfirmOp(clientId, 0));
        Assert.AreEqual(NfsConstants.Ok, confirmRes.Status);

        var renewRes = await DispatchAsync(ctx, new RenewOp(clientId));
        Assert.AreEqual(NfsConstants.Ok, renewRes.Status);
    }

    [TestMethod]
    public async Task Renew_StaleClientId_ReturnsStaleClientId()
    {
        var result = await DispatchAsync(new NfsRequestContext(), new RenewOp(99999ul));
        Assert.AreEqual(NfsConstants.ErrStaleClientId, result.Status);
    }

    [TestMethod]
    public async Task SecInfo_ReturnsAuthSys()
    {
        var ctx = new NfsRequestContext { CurrentFh = NfsFileHandle.Root };
        var result = await DispatchAsync(ctx, new SecInfoOp("anime-a"));
        Assert.AreEqual(NfsConstants.Ok, result.Status);
        var r = new XdrReader(result.Body);
        Assert.AreEqual(1u, r.ReadUInt32());
        Assert.AreEqual(1u, r.ReadUInt32());
    }

    [TestMethod]
    public async Task WriteOp_ReturnsRoFs()
    {
        var ctx = new NfsRequestContext();
        var write = new UnsupportedOp(NfsConstants.OpWrite, NfsConstants.ErrRoFs);
        var result = await DispatchAsync(ctx, write);
        Assert.AreEqual(NfsConstants.ErrRoFs, result.Status);
        Assert.AreEqual(NfsConstants.OpWrite, result.OpCode);
    }

    [TestMethod]
    public async Task MinorVersionMismatch_AbortsCompound()
    {
        var result = await _dispatcher.DispatchAsync(
            new NfsCompoundRequest("", MinorVersion: 1, [new PutRootFhOp()]),
            new NfsRequestContext());
        Assert.AreEqual(NfsConstants.ErrMinorVersMismatch, result.Status);
        Assert.AreEqual(0, result.Results.Count);
    }

    private async Task<DispatchResult> DispatchAsync(NfsRequestContext ctx, NfsOperation op)
    {
        var compound = await _dispatcher.DispatchAsync(new NfsCompoundRequest("", 0, [op]), ctx);
        var result = compound.Results[0];
        var reader = new XdrReader(result.Body);
        var status = reader.ReadUInt32();
        return new DispatchResult(status, result.OpCode, result.Body[4..]);
    }

    private record DispatchResult(uint Status, uint OpCode, byte[] Body);
}
