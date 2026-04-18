using System.Text;
using Moq;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.IntegrationTest.TestData;
using WebDav;

namespace SecondDimensionWatcherReDive.IntegrationTest.Methods;

[TestClass]
public sealed class WebDavClientLibraryTests
{
    private const string FileBody = "hello-from-webdav-client";
    private static readonly byte[] FileBytes = Encoding.UTF8.GetBytes(FileBody);

    private WebDavWebApplicationFactory _factory = null!;
    private HttpClient _httpClient = null!;
    private WebDavClient _client = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new WebDavWebApplicationFactory();
        _factory.ResetState();
        _httpClient = _factory.CreateBasicAuthClient();
        _client = new WebDavClient(_httpClient);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _httpClient.Dispose();
        _factory.Dispose();
    }

    private void SeedTree()
    {
        var f1 = WebDavMappingFixtures.NewMapping("/anime-a/file1.mkv", "/disk/file1.mkv");
        var sub = WebDavMappingFixtures.NewMapping("/anime-a/sub/extra.srt", "/disk/extra.srt");
        var f2 = WebDavMappingFixtures.NewMapping("/anime-b/file2.mkv", "/disk/file2.mkv");
        _factory.Mappings.AddRange(new[] { f1, sub, f2 });

        _factory.FileStoreMock
            .Setup(s => s.FileInfoAsync(f1.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileStoreInfo(false, f1.PhysicalPath, "file1.mkv",
                FileBytes.LongLength, WebDavMappingFixtures.FixedModified));
        _factory.FileStoreMock
            .Setup(s => s.FileInfoAsync(sub.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileStoreInfo(false, sub.PhysicalPath, "extra.srt",
                256L, WebDavMappingFixtures.FixedModified));
        _factory.FileStoreMock
            .Setup(s => s.FileInfoAsync(f2.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileStoreInfo(false, f2.PhysicalPath, "file2.mkv",
                2048L, WebDavMappingFixtures.FixedModified));
        _factory.FileStoreMock
            .Setup(s => s.OpenReadStreamAsync(f1.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(FileBytes, writable: false));
    }

    [TestMethod]
    public async Task Propfind_Root_Depth1_Lists_TopLevelChildren()
    {
        SeedTree();

        var response = await _client.Propfind("/webdav/", new PropfindParameters { ApplyTo = ApplyTo.Propfind.ResourceAndChildren });

        Assert.IsTrue(response.IsSuccessful, $"Propfind failed: {response.Description}");
        var hrefs = response.Resources.Select(r => r.Uri).ToList();
        CollectionAssert.Contains(hrefs, "/webdav/");
        CollectionAssert.Contains(hrefs, "/webdav/anime-a/");
        CollectionAssert.Contains(hrefs, "/webdav/anime-b/");

        var root = response.Resources.Single(r => r.Uri == "/webdav/");
        Assert.IsTrue(root.IsCollection);
    }

    [TestMethod]
    public async Task Propfind_Subdirectory_Depth1_Lists_FilesAndSubdirs()
    {
        SeedTree();

        var response = await _client.Propfind("/webdav/anime-a/",
            new PropfindParameters { ApplyTo = ApplyTo.Propfind.ResourceAndChildren });

        Assert.IsTrue(response.IsSuccessful);
        var fileResource = response.Resources.Single(r => r.Uri == "/webdav/anime-a/file1.mkv");
        Assert.IsFalse(fileResource.IsCollection);
        Assert.AreEqual(FileBytes.LongLength, fileResource.ContentLength);
        Assert.IsNotNull(fileResource.LastModifiedDate);
        Assert.IsNotNull(fileResource.ETag);

        var subDir = response.Resources.Single(r => r.Uri == "/webdav/anime-a/sub/");
        Assert.IsTrue(subDir.IsCollection);
    }

    [TestMethod]
    public async Task Propfind_File_Depth0_Returns_FileMetadata()
    {
        SeedTree();

        var response = await _client.Propfind("/webdav/anime-a/file1.mkv",
            new PropfindParameters { ApplyTo = ApplyTo.Propfind.ResourceOnly });

        Assert.IsTrue(response.IsSuccessful);
        Assert.AreEqual(1, response.Resources.Count);
        var resource = response.Resources.Single();
        Assert.AreEqual("/webdav/anime-a/file1.mkv", resource.Uri);
        Assert.IsFalse(resource.IsCollection);
        Assert.AreEqual(FileBytes.LongLength, resource.ContentLength);
        Assert.AreEqual("video/webm", resource.ContentType);
    }

    [TestMethod]
    public async Task Propfind_Missing_Resource_Returns_404()
    {
        SeedTree();

        var response = await _client.Propfind("/webdav/does/not/exist");

        Assert.IsFalse(response.IsSuccessful);
        Assert.AreEqual(404, response.StatusCode);
    }

    [TestMethod]
    public async Task GetRawFile_Returns_StreamWithBody()
    {
        SeedTree();

        using var response = await _client.GetRawFile("/webdav/anime-a/file1.mkv");

        Assert.IsTrue(response.IsSuccessful);
        using var ms = new MemoryStream();
        await response.Stream.CopyToAsync(ms);
        CollectionAssert.AreEqual(FileBytes, ms.ToArray());
    }

    [TestMethod]
    public async Task GetRawFile_Missing_Returns_404()
    {
        var response = await _client.GetRawFile("/webdav/missing.mkv");

        Assert.IsFalse(response.IsSuccessful);
        Assert.AreEqual(404, response.StatusCode);
    }

    [TestMethod]
    public async Task Mkcol_Returns_405_MethodNotAllowed()
    {
        var response = await _client.Mkcol("/webdav/new-folder/");

        Assert.IsFalse(response.IsSuccessful);
        Assert.AreEqual(405, response.StatusCode);
    }

    [TestMethod]
    public async Task PutFile_Returns_405_MethodNotAllowed()
    {
        using var content = new MemoryStream(FileBytes);
        var response = await _client.PutFile("/webdav/upload.bin", content);

        Assert.IsFalse(response.IsSuccessful);
        Assert.AreEqual(405, response.StatusCode);
    }

    [TestMethod]
    public async Task Delete_Returns_405_MethodNotAllowed()
    {
        SeedTree();

        var response = await _client.Delete("/webdav/anime-a/file1.mkv");

        Assert.IsFalse(response.IsSuccessful);
        Assert.AreEqual(405, response.StatusCode);
    }

    [TestMethod]
    public async Task Move_Returns_405_MethodNotAllowed()
    {
        SeedTree();

        var response = await _client.Move("/webdav/anime-a/file1.mkv", "/webdav/anime-a/renamed.mkv");

        Assert.IsFalse(response.IsSuccessful);
        Assert.AreEqual(405, response.StatusCode);
    }

    [TestMethod]
    public async Task Copy_Returns_405_MethodNotAllowed()
    {
        SeedTree();

        var response = await _client.Copy("/webdav/anime-a/file1.mkv", "/webdav/anime-a/copy.mkv");

        Assert.IsFalse(response.IsSuccessful);
        Assert.AreEqual(405, response.StatusCode);
    }

    [TestMethod]
    public async Task Propfind_Without_Auth_Returns_401()
    {
        SeedTree();
        using var anon = _factory.CreateUnauthenticatedClient();
        using var anonClient = new WebDavClient(anon);

        var response = await anonClient.Propfind("/webdav/");

        Assert.IsFalse(response.IsSuccessful);
        Assert.AreEqual(401, response.StatusCode);
    }
}
