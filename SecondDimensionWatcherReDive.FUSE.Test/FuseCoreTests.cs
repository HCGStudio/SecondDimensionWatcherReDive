using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SecondDimensionWatcherReDive.FUSE.Client;
using SecondDimensionWatcherReDive.FUSE.Fs;
using SecondDimensionWatcherReDive.FUSE.Native;

namespace SecondDimensionWatcherReDive.FUSE.Test;

[TestClass]
public sealed class FuseCoreTests
{
    [TestMethod]
    public void AttrCache_PrewarmsChildren_AndExpiresEntries()
    {
        var cache = new AttrCache(TimeSpan.FromMilliseconds(20));
        var child = new VfsEntry("episode.mkv", false, 42, DateTimeOffset.UtcNow);
        cache.PutList("/anime", [child]);

        Assert.IsTrue(cache.TryGetStat("/anime/episode.mkv", out var cached));
        Assert.AreEqual(child, cached);
        Thread.Sleep(40);
        Assert.IsFalse(cache.TryGetStat("/anime/episode.mkv", out _));
    }

    [TestMethod]
    public void FileHandleTable_AllocatesUniqueHandles_AndReleasesThem()
    {
        var table = new FileHandleTable();
        var first = table.Allocate("/one");
        var second = table.Allocate("/two");

        Assert.AreNotEqual(first, second);
        Assert.IsTrue(table.TryGet(first, out var file));
        Assert.AreEqual("/one", file.VirtualPath);
        table.Release(first);
        Assert.IsFalse(table.TryGet(first, out _));
    }

    [TestMethod]
    public async Task SdwClient_ReadRetriesServerErrors_AndPreservesRange()
    {
        var calls = 0;
        var handler = new StubHandler(request =>
        {
            calls++;
            Assert.AreEqual("bytes=5-7", request.Headers.Range?.ToString());
            return calls < 3
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent([10, 11, 12])
                };
        });
        using var client = new SdwClient(new Uri("http://localhost"), "user", "token", "test",
            NullLogger<SdwClient>.Instance, handler);
        var buffer = new byte[3];

        var read = await client.ReadAsync("/episode.mkv", 5, buffer, 0, 3, CancellationToken.None);

        Assert.AreEqual(3, calls);
        Assert.AreEqual(3, read);
        CollectionAssert.AreEqual(new byte[] { 10, 11, 12 }, buffer);
    }

    [TestMethod]
    public async Task SdwClient_ReadMapsHttpStatusesToErrno()
    {
        using var missing = CreateClient(HttpStatusCode.NotFound);
        using var failed = CreateClient(HttpStatusCode.BadGateway);
        var buffer = new byte[1];

        Assert.AreEqual(-Errno.ENOENT, await missing.ReadAsync("/missing", 0, buffer, 0, 1, CancellationToken.None));
        Assert.AreEqual(-Errno.EIO, await failed.ReadAsync("/failed", 0, buffer, 0, 1, CancellationToken.None));
    }

    private static SdwClient CreateClient(HttpStatusCode status) => new(new Uri("http://localhost"),
        "user", "token", "test", NullLogger<SdwClient>.Instance,
        new StubHandler(_ => new HttpResponseMessage(status)));

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
