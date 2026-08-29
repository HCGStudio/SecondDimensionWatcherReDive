using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SecondDimensionWatcherReDive.IntegrationTest.Plugins;

[TestClass]
public sealed class PluginApiTests
{
    [TestMethod]
    public async Task ManagementApi_RequiresPreviewApproval_AndSupportsLifecycle()
    {
        await using var factory = new WebDavWebApplicationFactory();
        using var client = factory.CreateJwtClient();
        var id = $"test.api-{Guid.NewGuid():N}";
        await using var package = CreatePackage(id, "1.0");
        using var form = new MultipartFormDataContent();
        form.Add(new StreamContent(package), "package", $"{id}.sdwpkg");

        using var previewResponse = await client.PostAsync("/api/plugins/preview", form);
        Assert.AreEqual(HttpStatusCode.OK, previewResponse.StatusCode,
            await previewResponse.Content.ReadAsStringAsync());
        using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        var previewRoot = preview.RootElement;
        Assert.AreEqual(id, previewRoot.GetProperty("manifest").GetProperty("id").GetString());
        Assert.IsFalse(previewRoot.GetProperty("isSignatureTrusted").GetBoolean());
        var capabilities = previewRoot.GetProperty("manifest").GetProperty("capabilities").Clone();

        using var installResponse = await client.PostAsJsonAsync("/api/plugins/install", new
        {
            previewToken = previewRoot.GetProperty("token").GetString(),
            expectedSha256 = previewRoot.GetProperty("packageSha256").GetString(),
            approvedCapabilities = capabilities
        });
        Assert.AreEqual(HttpStatusCode.OK, installResponse.StatusCode,
            await installResponse.Content.ReadAsStringAsync());

        using var enableResponse = await client.PostAsync($"/api/plugins/{id}/enable", null);
        Assert.AreEqual(HttpStatusCode.OK, enableResponse.StatusCode,
            await enableResponse.Content.ReadAsStringAsync());
        using var listResponse = await client.GetAsync("/api/plugins");
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var installed = list.RootElement.EnumerateArray().Single(item =>
            item.GetProperty("manifest").GetProperty("id").GetString() == id);
        Assert.IsTrue(installed.GetProperty("isEnabled").GetBoolean());
        Assert.IsFalse(installed.TryGetProperty("configuration", out _),
            "Plugin configuration must not leak from the management response.");

        using var disableResponse = await client.PostAsync($"/api/plugins/{id}/disable", null);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        using var deleteResponse = await client.DeleteAsync($"/api/plugins/{id}");
        Assert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode);
    }

    [TestMethod]
    public async Task ManagementApi_DisablesRemoteInstall_AndExplainsApiIncompatibility()
    {
        await using var factory = new WebDavWebApplicationFactory();
        using var client = factory.CreateJwtClient();
        using var remote = await client.PostAsJsonAsync("/api/plugins/preview-remote", new
        {
            url = "https://untrusted.example/plugin.js",
            expectedSha256 = (string?)null
        });
        Assert.AreEqual(HttpStatusCode.Forbidden, remote.StatusCode);
        StringAssert.Contains(await remote.Content.ReadAsStringAsync(), "remote_install_disabled");

        var id = $"test.future-{Guid.NewGuid():N}";
        await using var package = CreatePackage(id, "9.0");
        using var form = new MultipartFormDataContent();
        form.Add(new StreamContent(package), "package", $"{id}.sdwpkg");
        using var previewResponse = await client.PostAsync("/api/plugins/preview", form);
        using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        var root = preview.RootElement;
        using var install = await client.PostAsJsonAsync("/api/plugins/install", new
        {
            previewToken = root.GetProperty("token").GetString(),
            expectedSha256 = root.GetProperty("packageSha256").GetString(),
            approvedCapabilities = root.GetProperty("manifest").GetProperty("capabilities").Clone()
        });
        Assert.AreEqual(HttpStatusCode.OK, install.StatusCode, await install.Content.ReadAsStringAsync());

        using var enable = await client.PostAsync($"/api/plugins/{id}/enable", null);
        Assert.AreEqual(HttpStatusCode.Conflict, enable.StatusCode);
        var error = await enable.Content.ReadAsStringAsync();
        StringAssert.Contains(error, "incompatible");
        StringAssert.Contains(error, "9.0");
        await client.DeleteAsync($"/api/plugins/{id}?deleteData=true");
    }

    [TestMethod]
    public async Task ManagementApi_ReturnsBadRequestForMalformedPluginId()
    {
        await using var factory = new WebDavWebApplicationFactory();
        using var client = factory.CreateJwtClient();

        using var response = await client.PostAsync("/api/plugins/bad!id/enable", null);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "invalid_plugin_request");
    }

    private static MemoryStream CreatePackage(string id, string apiVersion)
    {
        const string script = "globalThis.sdwPlugin={handlers:{ping:()=>({ok:true})}};";
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script))).ToLowerInvariant();
        var manifest = JsonSerializer.Serialize(new
        {
            id,
            name = id,
            version = "1.0.0",
            apiVersion,
            entryPoint = "index.js",
            dependencies = Array.Empty<object>(),
            capabilities = new
            {
                networkDomains = Array.Empty<string>(),
                fileRoots = Array.Empty<string>(),
                notifications = false,
                downloadControl = false,
                storageAccess = false,
                backgroundTasks = false
            },
            platforms = new[] { "any" },
            integrity = new { files = new Dictionary<string, string> { ["index.js"] = digest } },
            providers = Array.Empty<object>(),
            dataVersion = 1
        });
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "manifest.json", manifest);
            WriteEntry(archive, "index.js", script);
        }
        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
