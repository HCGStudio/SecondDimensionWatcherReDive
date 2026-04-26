using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.IntegrationTest.TestData;
using SecondDimensionWatcherReDive.NFS;
using SecondDimensionWatcherReDive.NFS.Protocol;
using SecondDimensionWatcherReDive.NFS.Xdr;
using Moq;

namespace SecondDimensionWatcherReDive.IntegrationTest.Nfs;

[TestClass]
public sealed class NfsMountFlowTests
{
    private static readonly byte[] s_fileBytes = "hello-world-this-is-a-tiny-test-payload"u8.ToArray();
    private NfsTestHost _host = null!;
    private CancellationTokenSource _cts = null!;

    [TestInitialize]
    public void Setup()
    {
        _host = NfsTestHost.Start();
        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        SeedFiles();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        _cts.Cancel();
        await _host.DisposeAsync();
    }

    [TestMethod]
    public async Task NullProcedure_ReturnsAccepted()
    {
        await using var client = await NfsClientHarness.ConnectAsync(_host.Port, _cts.Token);
        var reply = await client.NullAsync(_cts.Token);
        Assert.IsTrue(reply.MessageAccepted);
        Assert.AreEqual(0u, reply.AcceptStatus);
    }

    [TestMethod]
    public async Task PutRootFh_GetFh_ReturnsRootHandle()
    {
        await using var client = await NfsClientHarness.ConnectAsync(_host.Port, _cts.Token);
        var reply = await client.CompoundAsync("",
            [NfsClientHarness.OpPutRootFh(), NfsClientHarness.OpGetFh()],
            _cts.Token);
        Assert.AreEqual(NfsConstants.Ok, reply.CompoundStatus);

        var (_, _, getFh) = ParseTwoOps(reply.ResArrayBytes);
        var r = new XdrReader(getFh);
        Assert.AreEqual(NfsConstants.Ok, r.ReadUInt32());
        var handle = NfsFileHandle.FromBytes(r.ReadOpaque());
        Assert.AreSame(NfsFileHandle.Root, handle);
    }

    [TestMethod]
    public async Task GetAttr_OnRoot_ReturnsTypeDir()
    {
        await using var client = await NfsClientHarness.ConnectAsync(_host.Port, _cts.Token);
        var bitmap = NfsAttributes.BitmapFromIds([NfsConstants.FattrType]);
        var reply = await client.CompoundAsync("",
            [NfsClientHarness.OpPutRootFh(), NfsClientHarness.OpGetAttr(bitmap)],
            _cts.Token);
        Assert.AreEqual(NfsConstants.Ok, reply.CompoundStatus);

        var bodies = ParseAllOps(reply.ResArrayBytes, expected: 2);
        var attrReader = new XdrReader(bodies[1]);
        Assert.AreEqual(NfsConstants.Ok, attrReader.ReadUInt32());
        var responseBitmap = attrReader.ReadUInt32Array();
        var inner = new XdrReader(attrReader.ReadOpaque());
        Assert.AreEqual(NfsConstants.Nf4Dir, inner.ReadUInt32());
        CollectionAssert.Contains(NfsAttributes.IdsFromBitmap(responseBitmap), NfsConstants.FattrType);
    }

    [TestMethod]
    public async Task Lookup_MissingName_ReturnsNoEnt()
    {
        await using var client = await NfsClientHarness.ConnectAsync(_host.Port, _cts.Token);
        var reply = await client.CompoundAsync("",
            [NfsClientHarness.OpPutRootFh(), NfsClientHarness.OpLookup("nope-not-here")],
            _cts.Token);
        Assert.AreEqual(NfsConstants.ErrNoEnt, reply.CompoundStatus);
    }

    [TestMethod]
    public async Task ReadDir_FromRoot_ReturnsAnimeDir()
    {
        await using var client = await NfsClientHarness.ConnectAsync(_host.Port, _cts.Token);
        var bitmap = NfsAttributes.BitmapFromIds([NfsConstants.FattrType]);
        var reply = await client.CompoundAsync("",
            [NfsClientHarness.OpPutRootFh(), NfsClientHarness.OpReadDir(0, 4096, 65536, bitmap)],
            _cts.Token);
        Assert.AreEqual(NfsConstants.Ok, reply.CompoundStatus);

        var bodies = ParseAllOps(reply.ResArrayBytes, expected: 2);
        var r = new XdrReader(bodies[1]);
        Assert.AreEqual(NfsConstants.Ok, r.ReadUInt32());
        _ = r.ReadFixedOpaque(8);
        var names = new List<string>();
        while (r.ReadBool())
        {
            _ = r.ReadUInt64();
            names.Add(r.ReadString());
            _ = r.ReadUInt32Array();
            _ = r.ReadOpaque();
        }
        var eof = r.ReadBool();
        CollectionAssert.Contains(names, "anime-a");
        Assert.IsTrue(eof);
    }

    [TestMethod]
    public async Task Read_FullFile_ReturnsBytesAndEof()
    {
        await using var client = await NfsClientHarness.ConnectAsync(_host.Port, _cts.Token);

        var reply = await client.CompoundAsync("",
            [
                NfsClientHarness.OpPutRootFh(),
                NfsClientHarness.OpLookup("anime-a"),
                NfsClientHarness.OpLookup("01.mkv"),
                NfsClientHarness.OpRead(NfsStateId.AnyState, 0, 1024),
            ],
            _cts.Token);

        Assert.AreEqual(NfsConstants.Ok, reply.CompoundStatus);
        var bodies = ParseAllOps(reply.ResArrayBytes, expected: 4);
        var r = new XdrReader(bodies[3]);
        Assert.AreEqual(NfsConstants.Ok, r.ReadUInt32());
        Assert.IsTrue(r.ReadBool());
        var data = r.ReadOpaque().ToArray();
        CollectionAssert.AreEqual(s_fileBytes, data);
    }

    [TestMethod]
    public async Task WriteOp_ReturnsRoFs()
    {
        await using var client = await NfsClientHarness.ConnectAsync(_host.Port, _cts.Token);
        var reply = await client.CompoundAsync("",
            [
                NfsClientHarness.OpPutRootFh(),
                NfsClientHarness.OpLookup("anime-a"),
                NfsClientHarness.OpLookup("01.mkv"),
                NfsClientHarness.OpWrite(NfsStateId.AnyState, 0, [0xCA, 0xFE]),
            ],
            _cts.Token);
        Assert.AreEqual(NfsConstants.ErrRoFs, reply.CompoundStatus);
    }

    private void SeedFiles()
    {
        var animePath = "/anime-a/01.mkv";
        var physical = "/disk/01.mkv";
        var mapping = WebDavMappingFixtures.NewMapping(animePath, physical);
        _host.Mappings.Add(mapping);

        _host.FileStoreMock
            .Setup(s => s.FileInfoAsync(physical, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileStoreInfo(false, physical, "01.mkv", s_fileBytes.Length, WebDavMappingFixtures.FixedModified));

        _host.FileStoreMock
            .Setup(s => s.OpenReadStreamAsync(physical, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(s_fileBytes, writable: false));
    }

    private static (uint Op1, byte[] Body1, byte[] Body2) ParseTwoOps(byte[] resArray)
    {
        var bodies = ParseAllOps(resArray, expected: 2);
        return (0, bodies[0], bodies[1]);
    }

    private static byte[][] ParseAllOps(byte[] resArray, int expected)
    {
        var reader = new XdrReader(resArray);
        var count = reader.ReadUInt32();
        if (count != expected)
            throw new InvalidOperationException($"Expected {expected} ops, got {count}");

        var output = new byte[count][];
        for (var i = 0; i < count; i++)
        {
            _ = reader.ReadUInt32();
            var startConsumed = reader.Consumed;
            // Each op result has an opcode-specific shape; we walk just the
            // status (uint32) and stop. Tests parse the body themselves.
            // To slice the body we need to know the op's full length, which
            // is op-specific. For our tests, only the LAST op carries
            // post-status data the test inspects, and we let the test
            // reader consume from the offset. Slice from startConsumed to
            // end of resArray for the LAST op; for prior ops we slice 4
            // bytes (status only) — they all carry status only in the
            // success-prior-to-failure or single-status-result shapes used
            // here.
            if (i == count - 1)
            {
                output[i] = resArray.AsMemory(startConsumed).ToArray();
                reader.Skip(resArray.Length - startConsumed);
            }
            else
            {
                output[i] = reader.ReadFixedOpaque(4).ToArray();
            }
        }
        return output;
    }
}
