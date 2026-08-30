using System.Net;
using System.Net.Http.Json;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.IntegrationTest.TestData;

namespace SecondDimensionWatcherReDive.IntegrationTest.Auth;

[TestClass]
public sealed class RoleAuthorizationTests
{
    [TestMethod]
    public async Task Viewer_CanReadFiles_ButCannotWriteContentPlaybackOrTasks()
    {
        using var factory = new WebDavWebApplicationFactory(role: UserRole.Viewer);
        factory.ResetState();
        factory.Mappings.Add(WebDavMappingFixtures.NewMapping(
            "/shows/episode.mkv", "/disk/episode.mkv"));
        using var client = factory.CreateJwtClient();

        using var read = await client.GetAsync("/api/vfs/stat?path=/shows/episode.mkv");
        using var addFeed = await client.PostAsJsonAsync("/api/feed", new
        {
            url = "https://example.test/feed.xml",
            name = "test"
        });
        using var playback = await client.PutAsJsonAsync("/api/playback/preferences", new
        {
            autoPlayNext = true
        });
        using var task = await client.PostAsync("/api/tasks/SyncFeed/run", null);

        Assert.AreEqual(HttpStatusCode.OK, read.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, addFeed.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, playback.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, task.StatusCode);
    }

    [TestMethod]
    public async Task Member_CannotRunAdministratorTask()
    {
        using var factory = new WebDavWebApplicationFactory(role: UserRole.Member);
        using var client = factory.CreateJwtClient();

        using var response = await client.PostAsync("/api/tasks/SyncFeed/run", null);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Member_CannotReadOrWriteSettingsManageUsersOrDeleteDownloadedFiles()
    {
        using var factory = new WebDavWebApplicationFactory(role: UserRole.Member);
        using var client = factory.CreateJwtClient();
        var animationId = Guid.NewGuid();

        using var readSettings = await client.GetAsync("/api/settings");
        using var writeSettings = await client.PatchAsJsonAsync("/api/settings", new { });
        using var manageUsers = await client.GetAsync("/api/accounts/users");
        using var deleteFiles = await client.DeleteAsync(
            $"/api/animationinfo/cancel/{animationId}?removeFile=true");

        Assert.AreEqual(HttpStatusCode.Forbidden, readSettings.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, writeSettings.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, manageUsers.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, deleteFiles.StatusCode);
    }

    [TestMethod]
    public async Task Viewer_CannotStartOrCancelDownloadsOrWriteChat()
    {
        using var factory = new WebDavWebApplicationFactory(role: UserRole.Viewer);
        using var client = factory.CreateJwtClient();
        var animationId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();

        using var start = await client.PostAsync(
            $"/api/animationinfo/download/{animationId}", null);
        using var cancel = await client.DeleteAsync(
            $"/api/animationinfo/cancel/{animationId}");
        using var createChat = await client.PostAsJsonAsync(
            "/api/chat/conversations", new { title = "blocked" });
        using var sendChat = await client.PostAsJsonAsync(
            $"/api/chat/conversations/{conversationId}/messages",
            new { content = "blocked" });

        Assert.AreEqual(HttpStatusCode.Forbidden, start.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, cancel.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, createChat.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, sendChat.StatusCode);
    }

    [TestMethod]
    public async Task MissingSessionToken_IsUnauthorized()
    {
        using var factory = new WebDavWebApplicationFactory();
        using var client = factory.CreateUnauthenticatedClient();

        using var response = await client.GetAsync("/api/feed");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task RevokedLoginSession_InvalidatesExistingAccessTokenImmediately()
    {
        using var factory = new WebDavWebApplicationFactory();
        factory.Mappings.Add(WebDavMappingFixtures.NewMapping(
            "/shows/episode.mkv", "/disk/episode.mkv"));
        using var client = factory.CreateJwtClient();
        using var before = await client.GetAsync("/api/vfs/stat?path=/shows/episode.mkv");
        Assert.AreEqual(HttpStatusCode.OK, before.StatusCode);

        factory.RevokeLoginSession();
        using var after = await client.GetAsync("/api/vfs/stat?path=/shows/episode.mkv");

        Assert.AreEqual(HttpStatusCode.Unauthorized, after.StatusCode);
    }
}
