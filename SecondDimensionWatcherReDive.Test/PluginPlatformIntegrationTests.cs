using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Plugin;
using SecondDimensionWatcherReDive.PluginPlatform;
using SecondDimensionWatcherReDive.Repositories;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class PluginPlatformIntegrationTests
{
    private const string PingScript = """
        'use strict';
        globalThis.sdwPlugin = { handlers: {
          ping(input, configuration) { return { value: input.value, marker: configuration.marker || null }; },
          inspectHost() { return { requireType: typeof require, fetchType: typeof fetch, getTypeType: typeof __sdwHost.GetType }; }
        }};
        """;

    [TestMethod]
    public async Task Enable_RejectsMissingDependenciesAndIncompatibleApi_WithClearReasons()
    {
        await using var fixture = new PluginPlatformFixture();
        var dependent = Manifest("test.dependent", capabilities: new PluginCapabilities()) with
        {
            Dependencies = [new PluginDependency("test.dependency", "1.2.0")]
        };
        await fixture.InstallAsync(dependent, PingScript);

        var missing = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.Manager.EnableAsync(dependent.Id, CancellationToken.None));
        StringAssert.Contains(missing.Message, "test.dependency");
        StringAssert.Contains(missing.Message, "not installed");

        await fixture.InstallAndEnableAsync(Manifest("test.dependency", version: "1.2.0"), PingScript);
        await fixture.Manager.EnableAsync(dependent.Id, CancellationToken.None);

        var incompatible = Manifest("test.future-api", apiVersion: "2.0");
        await fixture.InstallAsync(incompatible, PingScript);
        var apiError = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.Manager.EnableAsync(incompatible.Id, CancellationToken.None));
        StringAssert.Contains(apiError.Message, "API 2.0");
        StringAssert.Contains(apiError.Message, PluginApi.CurrentVersion);
    }

    [TestMethod]
    public async Task Install_RequiresExactApproval_AndTrustedSignatureByDefault()
    {
        using var signingKey = RSA.Create(2048);
        await using var fixture = new PluginPlatformFixture(options =>
        {
            options.AllowUnsignedLocalPackages = false;
            options.TrustedPublisherPublicKeys["test-publisher"] = signingKey.ExportSubjectPublicKeyInfoPem();
        });

        var unsigned = await fixture.PreviewAsync(Manifest("test.unsigned"), PingScript);
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => fixture.Manager.InstallPackageAsync(
            unsigned.Token,
            unsigned.PackageSha256,
            unsigned.Manifest.Capabilities,
            CancellationToken.None));

        var signedManifest = Sign(Manifest("test.signed"), PingScript, "test-publisher", signingKey);
        var signed = await fixture.PreviewAsync(signedManifest, PingScript);
        Assert.IsTrue(signed.IsSignatureTrusted, signed.SignatureStatus);

        var tampered = signedManifest with
        {
            Capabilities = signedManifest.Capabilities with { StorageAccess = true }
        };
        var tamperedPreview = await fixture.PreviewAsync(tampered, PingScript);
        Assert.IsFalse(tamperedPreview.IsSignatureTrusted,
            "Changing an approved capability must invalidate the publisher signature.");

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => fixture.Manager.InstallPackageAsync(
            signed.Token,
            signed.PackageSha256,
            signed.Manifest.Capabilities with { StorageAccess = true },
            CancellationToken.None));

        // A failed approval does not consume the staged package; exact approval succeeds.
        await fixture.Manager.InstallPackageAsync(
            signed.Token,
            signed.PackageSha256,
            signed.Manifest.Capabilities,
            CancellationToken.None);

        var signedWithAsset = Sign(
            Manifest("test.signed-asset"), PingScript, "test-publisher", signingKey,
            ("assets/prompt.txt", "publisher content"));
        await using var tamperedAssetPackage = BuildPackage(
            signedWithAsset, PingScript, ("assets/prompt.txt", "attacker content"));
        var assetError = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.Manager.PreviewPackageAsync(
                tamperedAssetPackage, "tampered-asset.sdwpkg", CancellationToken.None));
        StringAssert.Contains(assetError.Message, "assets/prompt.txt");
    }

    [TestMethod]
    public async Task PackageInspection_RejectsArchiveTraversalBeforeExecution()
    {
        await using var fixture = new PluginPlatformFixture();
        var manifest = WithIntegrity(Manifest("test.traversal"), PingScript);
        await using var package = BuildPackage(manifest, PingScript, ("../escape.js", "malicious"));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.Manager.PreviewPackageAsync(
            package,
            "traversal.sdwpkg",
            CancellationToken.None));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.RootPath, "escape.js")));
    }

    [TestMethod]
    public async Task PackageInspection_RejectsVersionTraversalBeforeExtraction()
    {
        await using var fixture = new PluginPlatformFixture();
        const string maliciousVersion = "1.0.0+/../../outside";
        var manifest = Manifest("test.version-traversal", version: maliciousVersion);
        var escapedPath = Path.GetFullPath(Path.Combine(
            fixture.RootPath, "packages", manifest.Id, maliciousVersion));

        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.PreviewAsync(manifest, PingScript));

        StringAssert.Contains(error.Message, "valid semantic version");
        Assert.IsFalse(Directory.Exists(escapedPath),
            "An invalid manifest version must be rejected before any extraction path is created.");
    }

    [TestMethod]
    public async Task Manifest_RejectsAmbiguousDependencyVersionsAndUnsafeDisplayIdentifiers()
    {
        await using var fixture = new PluginPlatformFixture();
        var invalidDependency = Manifest("test.invalid-dependency") with
        {
            Dependencies = [new PluginDependency("test.dependency", "1.0.0+/../../outside")]
        };
        var dependencyError = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.PreviewAsync(invalidDependency, PingScript));
        StringAssert.Contains(dependencyError.Message, "invalid minimum version");

        var invalidProvider = Manifest("test.invalid-provider", capabilities: new PluginCapabilities
        {
            Notifications = true
        }) with
        {
            Name = "Unsafe\u0001name",
            Providers = [new PluginProviderDeclaration
            {
                Kind = "notification",
                Name = "../../local",
                Handlers = new Dictionary<string, string> { ["send"] = "sendNotification" }
            }]
        };
        var providerError = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.PreviewAsync(invalidProvider, PingScript));
        StringAssert.Contains(providerError.Message, "ASCII identifier");
        StringAssert.Contains(providerError.Message, "control characters");
    }

    [TestMethod]
    public async Task Manifest_RejectsAmbiguousIdsAndUnknownMigrationStrategy()
    {
        await using var fixture = new PluginPlatformFixture();
        foreach (var invalidId in new[] { "foo.", "foo..bar" })
        {
            var idError = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                fixture.PreviewAsync(Manifest(invalidId), PingScript));
            StringAssert.Contains(idError.Message, "Plugin id");
        }

        var invalidMigration = Manifest("test.invalid-migration") with
        {
            DataMigration = new PluginDataMigration { Strategy = "garbage" }
        };
        var migrationError = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.PreviewAsync(invalidMigration, PingScript));
        StringAssert.Contains(migrationError.Message, "preserve");
        StringAssert.Contains(migrationError.Message, "reset");
    }

    [TestMethod]
    public async Task PackageStaging_EnforcesCountByteAndApprovalInputBounds()
    {
        var firstManifest = Manifest("test.stage-one");
        await using var probe = BuildPackage(firstManifest, PingScript);
        var packageBytes = probe.Length;

        await using (var countFixture = new PluginPlatformFixture(options =>
                     {
                         options.MaximumStagedPackages = 1;
                     }))
        {
            var preview = await countFixture.PreviewAsync(firstManifest, PingScript);
            var countError = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                countFixture.PreviewAsync(Manifest("test.stage-two"), PingScript));
            StringAssert.Contains(countError.Message, "staging limit");

            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                countFixture.Manager.InstallPackageAsync(null!, preview.PackageSha256,
                    preview.Manifest.Capabilities, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                countFixture.Manager.InstallPackageAsync(preview.Token, "not-a-checksum",
                    preview.Manifest.Capabilities, CancellationToken.None));
        }

        await using var byteFixture = new PluginPlatformFixture(options =>
        {
            options.MaximumStagedPackages = 4;
            options.MaximumPackageBytes = packageBytes + 1_024;
            options.MaximumStagedPackageBytes = packageBytes + 32;
        });
        await byteFixture.PreviewAsync(firstManifest, PingScript);
        var byteError = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            byteFixture.PreviewAsync(Manifest("test.stage-two"), PingScript));
        StringAssert.Contains(byteError.Message, "staging byte limit");
        Assert.HasCount(1, Directory.EnumerateFiles(
            Path.Combine(byteFixture.RootPath, "staging"), "*.sdwpkg").ToArray());
    }

    [TestMethod]
    public async Task Manifest_RejectsProviderWithoutCapabilityAndInvalidHandlerBeforeInstall()
    {
        await using var fixture = new PluginPlatformFixture();
        var missingCapability = Manifest("test.notification-capability") with
        {
            Providers = [new PluginProviderDeclaration
            {
                Kind = "notification",
                Name = "notifier",
                Handlers = new Dictionary<string, string> { ["send"] = "sendNotification" }
            }]
        };
        var capabilityError = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.PreviewAsync(missingCapability, PingScript));
        StringAssert.Contains(capabilityError.Message, "notifications capability");

        var invalidHandler = missingCapability with
        {
            Capabilities = new PluginCapabilities { Notifications = true },
            Providers = [missingCapability.Providers[0] with
            {
                Handlers = new Dictionary<string, string> { ["send"] = "../escape" }
            }]
        };
        var handlerError = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.PreviewAsync(invalidHandler, PingScript));
        StringAssert.Contains(handlerError.Message, "invalid handler name");
    }

    [TestMethod]
    public async Task Worker_DeniesUnapprovedNetworkAndFiles_AndDoesNotExposeClrOrWebGlobals()
    {
        const string script = """
            'use strict';
            globalThis.sdwPlugin = { handlers: {
              network() { return sdw.request('network.request', { method: 'GET', url: 'https://example.com/' }); },
              file() { return sdw.request('file.read', { path: '/etc/passwd' }); },
              inspect() { return {
                requireType: typeof require,
                fetchType: typeof fetch,
                exposedHostObjects: Object.getOwnPropertyNames(globalThis).filter(name => {
                  try { return globalThis[name] && typeof globalThis[name].GetType === 'function'; } catch { return false; }
                })
              }; },
              catchDenied() {
                try { sdw.request('network.request', { method: 'GET', url: 'https://example.com/' }); }
                catch (error) { return {
                  hostExceptionType: typeof error.hostException,
                  getTypeType: typeof error.GetType,
                  constructorName: error.constructor && error.constructor.name
                }; }
              }
            }};
            """;
        await using var fixture = new PluginPlatformFixture();
        var manifest = Manifest("test.denied");
        await fixture.InstallAndEnableAsync(manifest, script);

        var network = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.InvokeAsync(manifest.Id, "network"));
        StringAssert.Contains(network.Message, "not approved");
        var file = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.InvokeAsync(manifest.Id, "file"));
        StringAssert.Contains(file.Message, "No file roots");

        var inspected = await fixture.InvokeAsync(manifest.Id, "inspect");
        Assert.AreEqual("undefined", inspected.GetProperty("requireType").GetString());
        Assert.AreEqual("undefined", inspected.GetProperty("fetchType").GetString());
        Assert.AreEqual(0, inspected.GetProperty("exposedHostObjects").GetArrayLength());
        var caught = await fixture.InvokeAsync(manifest.Id, "catchDenied");
        Assert.AreEqual("undefined", caught.GetProperty("hostExceptionType").GetString());
        Assert.AreEqual("undefined", caught.GetProperty("getTypeType").GetString());
        Assert.AreEqual("Error", caught.GetProperty("constructorName").GetString());
    }

    [TestMethod]
    [DataRow("127.0.0.1")]
    [DataRow("169.254.169.254")]
    [DataRow("192.168.1.10")]
    [DataRow("192.88.99.2")]
    [DataRow("64:ff9b::a9fe:a9fe")]
    [DataRow("::192.168.1.10")]
    [DataRow("::ffff:0:127.0.0.1")]
    [DataRow("::ffff:0:169.254.169.254")]
    [DataRow("100:0:0:1::1")]
    [DataRow("2001:5::1")]
    [DataRow("2001:10::1")]
    [DataRow("3fff::1")]
    [DataRow("5f00::1")]
    [DataRow("fec0::1")]
    [DataRow("fe00::1")]
    [DataRow("4000::1")]
    public async Task NetworkCapability_RejectsApprovedHostResolvingToNonPublicAddress(string address)
    {
        using var networkHandler = PluginNetworkConnectionFactory.Create(
            new FixedDnsResolver(IPAddress.Parse(address)));
        await using var fixture = new PluginPlatformFixture(httpHandler: networkHandler);
        const string script = """
            globalThis.sdwPlugin={handlers:{request:()=>sdw.request('network.request',
              {method:'GET',url:'http://approved.example/resource'})}};
            """;
        var manifest = Manifest("test.ssrf", capabilities: new PluginCapabilities
        {
            NetworkDomains = ["approved.example"]
        });
        await fixture.InstallAndEnableAsync(manifest, script);

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.InvokeAsync(manifest.Id, "request"));
        StringAssert.Contains(error.Message, "non-public address");
    }

    [TestMethod]
    public async Task NetworkCapability_RejectsMixedPublicAndPrivateDnsAnswers()
    {
        using var networkHandler = PluginNetworkConnectionFactory.Create(
            new FixedDnsResolver(IPAddress.Parse("2606:4700:4700::1111"), IPAddress.Loopback));
        await using var fixture = new PluginPlatformFixture(httpHandler: networkHandler);
        const string script = """
            globalThis.sdwPlugin={handlers:{request:()=>sdw.request('network.request',
              {method:'GET',url:'http://approved.example/resource'})}};
            """;
        var manifest = Manifest("test.mixed-dns", capabilities: new PluginCapabilities
        {
            NetworkDomains = ["approved.example"]
        });
        await fixture.InstallAndEnableAsync(manifest, script);

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.InvokeAsync(manifest.Id, "request"));
        StringAssert.Contains(error.Message, "non-public address");
    }

    [TestMethod]
    [DataRow("2606:4700:4700::1111")]
    [DataRow("2001:4860:4860::8888")]
    public void NetworkCapability_AcceptsOrdinaryGlobalUnicastAddress(string address)
        => Assert.IsTrue(PluginNetworkConnectionFactory.IsPublicAddress(IPAddress.Parse(address)));

    [TestMethod]
    public async Task SafeFileAccess_RejectsDirectoryToSymlinkSwapBetweenApprovalAndOpen()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("The deterministic openat race test requires a POSIX host.");
            return;
        }
        var sandbox = Path.Combine(Path.GetTempPath(), $"sdw-plugin-race-{Guid.NewGuid():N}");
        var approved = Directory.CreateDirectory(Path.Combine(sandbox, "approved")).FullName;
        var live = Directory.CreateDirectory(Path.Combine(approved, "live")).FullName;
        var parked = Path.Combine(approved, "parked");
        var outside = Directory.CreateDirectory(Path.Combine(sandbox, "outside")).FullName;
        var target = Path.Combine(live, "secret.txt");
        var outsideSecret = Path.Combine(outside, "secret.txt");
        await File.WriteAllTextAsync(target, "approved");
        await File.WriteAllTextAsync(outsideSecret, "outside secret");
        var access = new PluginSafeFileAccess();
        var swapped = 0;
        access.BeforeOpenForTesting = () =>
        {
            if (Interlocked.Exchange(ref swapped, 1) != 0) return;
            Directory.Move(live, parked);
            Directory.CreateSymbolicLink(live, outside);
        };
        try
        {
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                access.ReadAsync(approved, target, 1_024, CancellationToken.None));
            Assert.AreEqual("outside secret", await File.ReadAllTextAsync(outsideSecret));
        }
        finally
        {
            if (Directory.Exists(live) || File.Exists(live)) Directory.Delete(live);
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [TestMethod]
    public async Task PluginDataQuota_RejectsGrowthAtomicallyAndPreservesExistingFile()
    {
        var (_, storageScript) = LoadExample("scoped-storage");
        await using var fixture = new PluginPlatformFixture(options =>
        {
            options.MaximumPluginDataBytes = 1_024;
            options.MaximumPluginDataFiles = 1;
            options.MaximumPluginDataPathDepth = 2;
            options.CircuitBreakerFailures = 20;
        });
        var manifest = StorageManifest("test.quota");
        await fixture.InstallAndEnableAsync(manifest, storageScript);
        var original = Enumerable.Repeat((byte)0x41, 700).ToArray();
        await fixture.InvokeAsync(manifest.Id, "seed", new
        {
            path = "state.bin",
            base64 = Convert.ToBase64String(original)
        });

        var extraError = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.InvokeAsync(manifest.Id, "seed", new
            {
                path = "extra.bin",
                base64 = Convert.ToBase64String(new byte[400])
            }));
        StringAssert.Contains(extraError.Message, "quota");
        var overwriteError = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.InvokeAsync(manifest.Id, "seed", new
            {
                path = "state.bin",
                base64 = Convert.ToBase64String(new byte[1_100])
            }));
        StringAssert.Contains(overwriteError.Message, "quota");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.InvokeAsync(manifest.Id, "seed", new
            {
                path = "one/two/three.bin",
                base64 = Convert.ToBase64String(new byte[1])
            }));

        var dataRoot = Path.Combine(fixture.RootPath, "data", manifest.Id);
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(Path.Combine(dataRoot, "state.bin")));
        Assert.IsFalse(File.Exists(Path.Combine(dataRoot, "extra.bin")));
    }

    [TestMethod]
    public async Task Worker_ReportsScriptErrorsThroughProtocol_AndLeavesHealthyPluginAvailable()
    {
        const string crashingScript = """
            'use strict';
            globalThis.sdwPlugin = { handlers: {
              crash() { throw new Error('intentional crash'); }
            }};
            """;
        await using var fixture = new PluginPlatformFixture();
        var crashing = Manifest("test.crash-protocol");
        await fixture.InstallAndEnableAsync(crashing, crashingScript);
        var healthy = Manifest("test.crash-healthy");
        await fixture.InstallAndEnableAsync(healthy, PingScript);

        var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.InvokeAsync(crashing.Id, "crash"));
        StringAssert.Contains(failure.Message, "intentional crash");

        var result = await fixture.InvokeAsync(healthy.Id, "ping", new { value = 7 });
        Assert.AreEqual(7, result.GetProperty("value").GetInt32());
    }

    [TestMethod]
    public async Task WorkerCapacity_RejectionsDoNotDegradePluginHealth()
    {
        const string timeoutScript = """
            'use strict';
            globalThis.sdwPlugin = { handlers: {
              timeout() { while (true) {} }
            }};
            """;
        await using var fixture = new PluginPlatformFixture(options =>
        {
            options.InvocationTimeoutMilliseconds = 2_000;
            options.MaximumWorkerCpuMilliseconds = 1_500;
            options.MaximumWorkerMemoryMegabytes = 256;
            options.MaximumConcurrentWorkers = 1;
            options.MaximumConcurrentWorkersPerPlugin = 1;
            options.CircuitBreakerFailures = 3;
        });
        var hostile = Manifest("test.capacity-hostile");
        await fixture.InstallAndEnableAsync(hostile, timeoutScript);
        var healthy = Manifest("test.capacity-healthy");
        await fixture.InstallAndEnableAsync(healthy, PingScript);

        var firstTimeout = fixture.InvokeAsync(hostile.Id, "timeout");
        await Task.Delay(25);
        var rejected = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            try
            {
                await fixture.InvokeAsync(hostile.Id, "timeout");
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }));
        Assert.IsTrue(rejected.All(exception => exception is PluginCapacityExceededException));
        await Assert.ThrowsExactlyAsync<PluginCapacityExceededException>(() =>
            fixture.InvokeAsync(healthy.Id, "ping", new { value = 1 }));
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => firstTimeout);
        var healthAfterCapacity = (await fixture.Manager.GetAllAsync(CancellationToken.None))
            .Single(plugin => plugin.Manifest.Id == hostile.Id).Health;
        Assert.AreEqual(1, healthAfterCapacity.ConsecutiveFailures,
            "Capacity rejections must not degrade plugin health.");
        var healthyHealth = (await fixture.Manager.GetAllAsync(CancellationToken.None))
            .Single(plugin => plugin.Manifest.Id == healthy.Id).Health;
        Assert.AreEqual(0, healthyHealth.ConsecutiveFailures,
            "A global capacity rejection must not be attributed to another plugin.");
    }

    [TestMethod]
    public async Task Worker_ContainsTimeoutAndResourceExhaustion_ThenOpensCircuit()
    {
        const string hostileScript = """
            'use strict';
            globalThis.sdwPlugin = { handlers: {
              timeout() { while (true) {} },
              memory() { const values = []; while (true) values.push(new ArrayBuffer(1048576)); }
            }};
            """;
        await using var fixture = new PluginPlatformFixture(options =>
        {
            options.InvocationTimeoutMilliseconds = 400;
            options.MaximumWorkerCpuMilliseconds = 300;
            options.MaximumWorkerMemoryMegabytes = 64;
            options.CircuitBreakerFailures = 3;
        });
        var hostile = Manifest("test.resource-hostile");
        await fixture.InstallAndEnableAsync(hostile, hostileScript);
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => fixture.InvokeAsync(hostile.Id, "timeout"));
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => fixture.InvokeAsync(hostile.Id, "timeout"));
        var memoryFailure = await Assert.ThrowsAsync<Exception>(() => fixture.InvokeAsync(hostile.Id, "memory"));
        Assert.IsTrue(memoryFailure is TimeoutException or InvalidOperationException,
            $"Unexpected resource failure type: {memoryFailure.GetType().Name}");
        Assert.IsLessThan(TimeSpan.FromSeconds(8), stopwatch.Elapsed);

        var circuit = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.InvokeAsync(hostile.Id, "timeout"));
        StringAssert.Contains(circuit.Message, "circuit is open");
    }

    [TestMethod]
    public async Task NotificationAndStorageExamples_PassProviderIntegrationFlow()
    {
        var http = new RecordingHttpMessageHandler();
        await using var fixture = new PluginPlatformFixture(httpHandler: http);

        var (webhook, webhookScript) = LoadExample("webhook");
        await fixture.InstallAndEnableAsync(webhook, webhookScript);
        await fixture.Manager.UpdateConfigurationAsync(
            webhook.Id,
            JsonSerializer.SerializeToElement(new { url = "https://hooks.example.com/sdw" }),
            CancellationToken.None);

        var (storage, storageScript) = LoadExample("scoped-storage");
        await fixture.InstallAndEnableAsync(storage, storageScript);

        var registry = new PluginProviderRegistry(fixture.Manager);
        var notificationProvider = registry.GetNotificationProviders().Single();
        Assert.AreEqual("plugin:example.webhook:webhook", notificationProvider.Name);
        await notificationProvider.SendAsync(new PluginNotification("Ready", "Library scan completed"),
            CancellationToken.None);
        Assert.AreEqual("https://hooks.example.com/sdw", http.LastRequestUri?.ToString());
        StringAssert.Contains(http.LastBody ?? string.Empty, "Library scan completed");

        var bytes = Encoding.UTF8.GetBytes("isolated storage");
        await fixture.Manager.InvokeAsync(storage.Id, "seed", JsonSerializer.SerializeToElement(new
        {
            path = "folder/item.txt",
            base64 = Convert.ToBase64String(bytes)
        }), CancellationToken.None);
        var fileStore = registry.GetFileStores().Single();
        Assert.AreEqual("plugin:example.scoped-storage:example-scoped", fileStore.Name);
        Assert.IsTrue(await fileStore.ExistAsync("folder/item.txt", CancellationToken.None));
        var info = await fileStore.FileInfoAsync("folder/item.txt", CancellationToken.None);
        Assert.AreEqual(bytes.Length, info.Length);
        await using var stream = await fileStore.OpenReadStreamAsync("folder/item.txt", CancellationToken.None);
        using var reader = new StreamReader(stream);
        Assert.AreEqual("isolated storage", await reader.ReadToEndAsync());
        var listed = await fileStore.EnumerateDirectory("folder").ToListAsync();
        Assert.HasCount(1, listed);
        Assert.AreEqual("item.txt", listed[0].FileName);
    }

    [TestMethod]
    public async Task ProviderIdentity_IsQualifiedAndStableAcrossDisableUninstallAndReinstall()
    {
        var (_, storageScript) = LoadExample("scoped-storage");
        await using var fixture = new PluginPlatformFixture();
        var first = StorageManifest("test.provider-one", "shared");
        var second = StorageManifest("test.provider-two", "shared");
        await fixture.InstallAndEnableAsync(first, storageScript);
        await fixture.InstallAndEnableAsync(second, storageScript);
        var registry = new PluginProviderRegistry(fixture.Manager);

        CollectionAssert.AreEquivalent(
            new[] { "plugin:test.provider-one:shared", "plugin:test.provider-two:shared" },
            registry.GetFileStores().Select(store => store.Name).ToArray());
        Assert.IsFalse(registry.GetFileStores().Any(store => store.Name == "local"));

        await fixture.Manager.DisableAsync(first.Id, CancellationToken.None);
        CollectionAssert.AreEqual(
            new[] { "plugin:test.provider-two:shared" },
            registry.GetFileStores().Select(store => store.Name).ToArray());

        await fixture.Manager.UninstallAsync(first.Id, deleteData: false, CancellationToken.None);
        await fixture.InstallAndEnableAsync(first, storageScript);
        Assert.IsTrue(registry.GetFileStores().Any(store =>
            store.Name == "plugin:test.provider-one:shared"));
    }

    [TestMethod]
    public async Task DeleteDataUninstall_DoesNotDeleteAnotherPluginWithMigrationLikeId()
    {
        var (_, storageScript) = LoadExample("scoped-storage");
        await using var fixture = new PluginPlatformFixture();
        var removed = StorageManifest("test.a");
        var neighbor = StorageManifest("test.a.migration-b");
        await fixture.InstallAndEnableAsync(removed, storageScript);
        await fixture.InstallAndEnableAsync(neighbor, storageScript);
        await fixture.InvokeAsync(neighbor.Id, "seed", new
        {
            path = "state.txt",
            base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("neighbor"))
        });

        await fixture.Manager.UninstallAsync(removed.Id, deleteData: true, CancellationToken.None);

        var exists = await fixture.InvokeAsync(neighbor.Id, "exists", new { path = "state.txt" });
        Assert.IsTrue(exists.GetProperty("exists").GetBoolean());
        Assert.IsTrue(Directory.Exists(Path.Combine(fixture.RootPath, "data", neighbor.Id)));
    }

    [TestMethod]
    public async Task UpgradeAndUninstall_ApplyExplicitDataAndConfigurationStrategy()
    {
        const string storageScript = """
            'use strict';
            globalThis.sdwPlugin = { handlers: {
              config(input, configuration) { return configuration; },
              seed(input) { return sdw.request('data.write', input); },
              exists(input) { return sdw.request('data.exists', input); }
            }};
            """;
        await using var fixture = new PluginPlatformFixture();
        var versionOne = StorageManifest("test.lifecycle");
        await fixture.InstallAndEnableAsync(versionOne, storageScript);
        await fixture.Manager.UpdateConfigurationAsync(versionOne.Id,
            JsonSerializer.SerializeToElement(new { marker = "preserved" }), CancellationToken.None);
        await fixture.Manager.InvokeAsync(versionOne.Id, "seed", JsonSerializer.SerializeToElement(new
        {
            path = "state.txt",
            base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("old"))
        }), CancellationToken.None);

        await fixture.Manager.UninstallAsync(versionOne.Id, deleteData: false, CancellationToken.None);
        await fixture.InstallAndEnableAsync(versionOne, storageScript);
        var restoredConfig = await fixture.InvokeAsync(versionOne.Id, "config");
        Assert.AreEqual("preserved", restoredConfig.GetProperty("marker").GetString());
        var retained = await fixture.InvokeAsync(versionOne.Id, "exists", new { path = "state.txt" });
        Assert.IsTrue(retained.GetProperty("exists").GetBoolean());

        var unsafeUpgrade = versionOne with { Version = "2.0.0", DataVersion = 2 };
        var unsafePreview = await fixture.PreviewAsync(unsafeUpgrade, storageScript);
        var migrationError = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.Manager.UpgradeAsync(versionOne.Id, unsafePreview.Token, unsafePreview.PackageSha256,
                unsafePreview.Manifest.Capabilities, CancellationToken.None));
        StringAssert.Contains(migrationError.Message, "dataMigration.strategy = 'reset'");

        var resetUpgrade = unsafeUpgrade with
        {
            DataMigration = new PluginDataMigration
            {
                Strategy = "reset",
                Description = "Version 2 intentionally starts with an empty cache."
            }
        };
        var resetPreview = await fixture.PreviewAsync(resetUpgrade, storageScript);
        await fixture.Manager.UpgradeAsync(versionOne.Id, resetPreview.Token, resetPreview.PackageSha256,
            resetPreview.Manifest.Capabilities, CancellationToken.None);
        await fixture.Manager.EnableAsync(versionOne.Id, CancellationToken.None);
        var reset = await fixture.InvokeAsync(versionOne.Id, "exists", new { path = "state.txt" });
        Assert.IsFalse(reset.GetProperty("exists").GetBoolean());
        var preservedConfig = await fixture.InvokeAsync(versionOne.Id, "config");
        Assert.AreEqual("preserved", preservedConfig.GetProperty("marker").GetString());
    }

    [TestMethod]
    public async Task UpgradeAndUninstall_CancelAndDrainOldWorkersBeforeMovingDataOrPackage()
    {
        var (_, baseStorageScript) = LoadExample("scoped-storage");
        var slowStorageScript = baseStorageScript + """

            globalThis.sdwPlugin.handlers.slowSeed = function(input) {
              const deadline = Date.now() + 3000;
              while (Date.now() < deadline) {}
              return sdw.request('data.write', input);
            };
            """;
        await using var fixture = new PluginPlatformFixture(options =>
        {
            options.InvocationTimeoutMilliseconds = 8_000;
            options.MaximumWorkerCpuMilliseconds = 7_000;
        });
        var versionOne = StorageManifest("test.lifecycle-drain");
        await fixture.InstallAndEnableAsync(versionOne, slowStorageScript);
        await fixture.InvokeAsync(versionOne.Id, "seed", new
        {
            path = "old.txt",
            base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("old"))
        });

        var oldInvocation = fixture.InvokeAsync(versionOne.Id, "slowSeed", new
        {
            path = "stale-after-upgrade.txt",
            base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("stale"))
        });
        await Task.Delay(150);
        var versionTwo = versionOne with
        {
            Version = "2.0.0",
            DataVersion = 2,
            DataMigration = new PluginDataMigration { Strategy = "reset" }
        };
        var preview = await fixture.PreviewAsync(versionTwo, slowStorageScript);
        await fixture.Manager.UpgradeAsync(versionOne.Id, preview.Token, preview.PackageSha256,
            preview.Manifest.Capabilities, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<PluginInvocationInterruptedException>(() => oldInvocation);
        await fixture.Manager.EnableAsync(versionOne.Id, CancellationToken.None);
        Assert.IsFalse(File.Exists(Path.Combine(
            fixture.RootPath, "data", versionOne.Id, "stale-after-upgrade.txt")));

        var uninstallInvocation = fixture.InvokeAsync(versionOne.Id, "slowSeed", new
        {
            path = "stale-after-uninstall.txt",
            base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("stale"))
        });
        await Task.Delay(150);
        await fixture.Manager.UninstallAsync(versionOne.Id, deleteData: true, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<PluginInvocationInterruptedException>(() => uninstallInvocation);
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.RootPath, "data", versionOne.Id)));
        Assert.IsFalse(Directory.Exists(Path.Combine(
            fixture.RootPath, "packages", versionOne.Id, versionTwo.Version)));
    }

    [TestMethod]
    public async Task CascadingDisableAndUninstall_CancelDependentWorkersBeforeReturning()
    {
        var (_, baseStorageScript) = LoadExample("scoped-storage");
        var slowStorageScript = baseStorageScript + """

            globalThis.sdwPlugin.handlers.slowSeed = function(input) {
              const deadline = Date.now() + 3000;
              while (Date.now() < deadline) {}
              return sdw.request('data.write', input);
            };
            """;
        await using var fixture = new PluginPlatformFixture(options =>
        {
            options.InvocationTimeoutMilliseconds = 8_000;
            options.MaximumWorkerCpuMilliseconds = 7_000;
        });
        var dependency = Manifest("test.cascade-root");
        var dependent = StorageManifest("test.cascade-dependent") with
        {
            Dependencies = [new PluginDependency(dependency.Id, dependency.Version)]
        };
        await fixture.InstallAndEnableAsync(dependency, PingScript);
        await fixture.InstallAndEnableAsync(dependent, slowStorageScript);

        var disableInvocation = fixture.InvokeAsync(dependent.Id, "slowSeed", new
        {
            path = "stale-after-disable.txt",
            base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("stale"))
        });
        await Task.Delay(150);
        await fixture.Manager.DisableAsync(dependency.Id, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<PluginInvocationInterruptedException>(() => disableInvocation);
        Assert.IsFalse((await fixture.Manager.GetAllAsync(CancellationToken.None))
            .Single(plugin => plugin.Manifest.Id == dependent.Id).IsEnabled);
        Assert.IsFalse(File.Exists(Path.Combine(
            fixture.RootPath, "data", dependent.Id, "stale-after-disable.txt")));

        await fixture.Manager.EnableAsync(dependency.Id, CancellationToken.None);
        await fixture.Manager.EnableAsync(dependent.Id, CancellationToken.None);
        var uninstallInvocation = fixture.InvokeAsync(dependent.Id, "slowSeed", new
        {
            path = "stale-after-uninstall.txt",
            base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("stale"))
        });
        await Task.Delay(150);
        await fixture.Manager.UninstallAsync(dependency.Id, deleteData: true, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<PluginInvocationInterruptedException>(() => uninstallInvocation);
        Assert.IsFalse((await fixture.Manager.GetAllAsync(CancellationToken.None))
            .Single(plugin => plugin.Manifest.Id == dependent.Id).IsEnabled);
        Assert.IsFalse(File.Exists(Path.Combine(
            fixture.RootPath, "data", dependent.Id, "stale-after-uninstall.txt")));
    }

    [TestMethod]
    public async Task StartupRecovery_RollsBackPreparedUninstall()
    {
        var (_, storageScript) = LoadExample("scoped-storage");
        await using var fixture = new PluginPlatformFixture();
        var manifest = StorageManifest("test.recover-uninstall-prepared");
        var unaffected = Manifest("test.recover-unaffected");
        await fixture.InstallAndEnableAsync(manifest, storageScript);
        await fixture.InstallAndEnableAsync(unaffected, PingScript);
        await fixture.InvokeAsync(manifest.Id, "seed", new
        {
            path = "state.txt",
            base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("preserve"))
        });
        fixture.Manager.LifecycleCheckpointForTesting = checkpoint =>
        {
            if (checkpoint == PluginLifecycleCheckpoint.AfterMove)
                throw new PluginProcessCrashSimulationException();
        };

        await Assert.ThrowsExactlyAsync<PluginProcessCrashSimulationException>(() =>
            fixture.Manager.UninstallAsync(manifest.Id, deleteData: true, CancellationToken.None));
        GetSingleLifecycleTransaction(fixture.RootPath, "uninstall", manifest.Id);
        Assert.IsFalse(Directory.Exists(Path.Combine(
            fixture.RootPath, "packages", manifest.Id, manifest.Version)));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.RootPath, "data", manifest.Id)));
        var overlapError = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.Manager.UninstallAsync(unaffected.Id, deleteData: true, CancellationToken.None));
        StringAssert.Contains(overlapError.Message, "pending lifecycle recovery");
        var invokeError = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.InvokeAsync(unaffected.Id, "ping", new { value = 1 }));
        StringAssert.Contains(invokeError.Message, "pending lifecycle recovery");

        var restarted = fixture.CreateRestartedManager();
        var restartedPlugins = await restarted.GetAllAsync(CancellationToken.None);
        var restored = restartedPlugins.Single(plugin =>
            plugin.Manifest.Id == manifest.Id);
        Assert.AreEqual(manifest.Version, restored.Manifest.Version);
        Assert.IsTrue(restored.IsEnabled);
        Assert.IsTrue(restartedPlugins.Single(plugin => plugin.Manifest.Id == unaffected.Id).IsEnabled);
        var exists = await restarted.InvokeAsync(manifest.Id, "exists",
            JsonSerializer.SerializeToElement(new { path = "state.txt" }), CancellationToken.None);
        Assert.IsTrue(exists.GetProperty("exists").GetBoolean());
        AssertNoLifecycleTransactions(fixture.RootPath);
    }

    [TestMethod]
    public async Task StartupRecovery_FinalizesCommittedUninstallWithPartiallyMissingPayload()
    {
        var (_, storageScript) = LoadExample("scoped-storage");
        await using var fixture = new PluginPlatformFixture();
        var manifest = StorageManifest("test.recover-uninstall-committed");
        await fixture.InstallAndEnableAsync(manifest, storageScript);
        await fixture.InvokeAsync(manifest.Id, "seed", new
        {
            path = "state.txt",
            base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("delete"))
        });
        fixture.Manager.LifecycleCheckpointForTesting = checkpoint =>
        {
            if (checkpoint != PluginLifecycleCheckpoint.AfterCommit) return;
            var transactionPath = GetSingleLifecycleTransaction(
                fixture.RootPath, "uninstall", manifest.Id);
            Directory.Delete(Path.Combine(transactionPath, "package"), recursive: true);
            File.Delete(Path.Combine(transactionPath, "data", "state.txt"));
            throw new PluginProcessCrashSimulationException();
        };

        await Assert.ThrowsExactlyAsync<PluginProcessCrashSimulationException>(() =>
            fixture.Manager.UninstallAsync(manifest.Id, deleteData: true, CancellationToken.None));
        var pendingPreview = await fixture.PreviewAsync(manifest, storageScript);
        var pendingError = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.Manager.InstallPackageAsync(pendingPreview.Token, pendingPreview.PackageSha256,
                pendingPreview.Manifest.Capabilities, CancellationToken.None));
        StringAssert.Contains(pendingError.Message, "pending lifecycle recovery");

        var restarted = fixture.CreateRestartedManager();
        Assert.IsEmpty(await restarted.GetAllAsync(CancellationToken.None));
        Assert.IsFalse(Directory.Exists(Path.Combine(
            fixture.RootPath, "packages", manifest.Id, manifest.Version)));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.RootPath, "data", manifest.Id)));
        AssertNoLifecycleTransactions(fixture.RootPath);
    }

    [TestMethod]
    public async Task StartupRecovery_RollsBackPreparedResetUpgrade()
    {
        var (_, storageScript) = LoadExample("scoped-storage");
        await using var fixture = new PluginPlatformFixture();
        var versionOne = StorageManifest("test.recover-upgrade-prepared");
        await fixture.InstallAndEnableAsync(versionOne, storageScript);
        await fixture.InvokeAsync(versionOne.Id, "seed", new
        {
            path = "state.txt",
            base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("preserve"))
        });
        var versionTwo = versionOne with
        {
            Version = "2.0.0",
            DataVersion = 2,
            DataMigration = new PluginDataMigration { Strategy = "reset" }
        };
        var preview = await fixture.PreviewAsync(versionTwo, storageScript);
        fixture.Manager.LifecycleCheckpointForTesting = checkpoint =>
        {
            if (checkpoint == PluginLifecycleCheckpoint.AfterMove)
                throw new PluginProcessCrashSimulationException();
        };

        await Assert.ThrowsExactlyAsync<PluginProcessCrashSimulationException>(() =>
            fixture.Manager.UpgradeAsync(versionOne.Id, preview.Token, preview.PackageSha256,
                preview.Manifest.Capabilities, CancellationToken.None));
        GetSingleLifecycleTransaction(fixture.RootPath, "upgrade", versionOne.Id);
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.RootPath, "data", versionOne.Id)));

        var restarted = fixture.CreateRestartedManager();
        var restored = (await restarted.GetAllAsync(CancellationToken.None)).Single(plugin =>
            plugin.Manifest.Id == versionOne.Id);
        Assert.AreEqual(versionOne.Version, restored.Manifest.Version);
        Assert.IsTrue(restored.IsEnabled);
        var exists = await restarted.InvokeAsync(versionOne.Id, "exists",
            JsonSerializer.SerializeToElement(new { path = "state.txt" }), CancellationToken.None);
        Assert.IsTrue(exists.GetProperty("exists").GetBoolean());
        Assert.IsFalse(Directory.Exists(Path.Combine(
            fixture.RootPath, "packages", versionOne.Id, versionTwo.Version)));
        AssertNoLifecycleTransactions(fixture.RootPath);
    }

    [TestMethod]
    public async Task StartupRecovery_FinalizesCommittedResetUpgradeAndBlocksOverlappingLifecycle()
    {
        var (_, storageScript) = LoadExample("scoped-storage");
        await using var fixture = new PluginPlatformFixture();
        var versionOne = StorageManifest("test.recover-upgrade-committed");
        await fixture.InstallAndEnableAsync(versionOne, storageScript);
        await fixture.InvokeAsync(versionOne.Id, "seed", new
        {
            path = "state.txt",
            base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("delete"))
        });
        var versionTwo = versionOne with
        {
            Version = "2.0.0",
            DataVersion = 2,
            DataMigration = new PluginDataMigration { Strategy = "reset" }
        };
        var preview = await fixture.PreviewAsync(versionTwo, storageScript);
        fixture.Manager.LifecycleCheckpointForTesting = checkpoint =>
        {
            if (checkpoint != PluginLifecycleCheckpoint.AfterCommit) return;
            var transactionPath = GetSingleLifecycleTransaction(
                fixture.RootPath, "upgrade", versionOne.Id);
            File.Delete(Path.Combine(transactionPath, "data", "state.txt"));
            throw new IOException("Simulated committed-cleanup interruption.");
        };

        await Assert.ThrowsExactlyAsync<IOException>(() => fixture.Manager.UpgradeAsync(
            versionOne.Id, preview.Token, preview.PackageSha256,
            preview.Manifest.Capabilities, CancellationToken.None));
        var pendingInvocation = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.InvokeAsync(versionOne.Id, "exists", new { path = "state.txt" }));
        StringAssert.Contains(pendingInvocation.Message, "pending lifecycle recovery");
        var pendingUninstall = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.Manager.UninstallAsync(versionOne.Id, deleteData: true, CancellationToken.None));
        StringAssert.Contains(pendingUninstall.Message, "pending lifecycle recovery");
        var versionThree = versionTwo with { Version = "3.0.0" };
        var versionThreePreview = await fixture.PreviewAsync(versionThree, storageScript);
        var pendingUpgrade = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.Manager.UpgradeAsync(versionOne.Id, versionThreePreview.Token,
                versionThreePreview.PackageSha256, versionThreePreview.Manifest.Capabilities,
                CancellationToken.None));
        StringAssert.Contains(pendingUpgrade.Message, "pending lifecycle recovery");
        GetSingleLifecycleTransaction(fixture.RootPath, "upgrade", versionOne.Id);

        var restarted = fixture.CreateRestartedManager();
        var installed = (await restarted.GetAllAsync(CancellationToken.None)).Single(plugin =>
            plugin.Manifest.Id == versionOne.Id);
        Assert.AreEqual(versionTwo.Version, installed.Manifest.Version);
        Assert.IsFalse(installed.IsEnabled);
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.RootPath, "data", versionOne.Id)));
        Assert.IsFalse(Directory.Exists(Path.Combine(
            fixture.RootPath, "packages", versionOne.Id, versionOne.Version)));
        Assert.IsTrue(Directory.Exists(Path.Combine(
            fixture.RootPath, "packages", versionOne.Id, versionTwo.Version)));
        AssertNoLifecycleTransactions(fixture.RootPath);

        await restarted.UninstallAsync(versionOne.Id, deleteData: true, CancellationToken.None);
        var restartedAgain = fixture.CreateRestartedManager();
        Assert.IsEmpty(await restartedAgain.GetAllAsync(CancellationToken.None));
        Assert.IsFalse(Directory.Exists(Path.Combine(
            fixture.RootPath, "packages", versionOne.Id, versionTwo.Version)));
        AssertNoLifecycleTransactions(fixture.RootPath);
    }

    [TestMethod]
    public async Task Invoke_AcquiresLifecycleLeaseBeforeManagementGateCanUninstallStaleEntry()
    {
        const string slowScript = """
            globalThis.sdwPlugin={handlers:{slow:()=>{const end=Date.now()+3000;while(Date.now()<end){};return {ok:true};}}};
            """;
        await using var fixture = new PluginPlatformFixture(options =>
        {
            options.InvocationTimeoutMilliseconds = 8_000;
            options.MaximumWorkerCpuMilliseconds = 7_000;
        });
        var manifest = Manifest("test.invoke-ordering");
        await fixture.InstallAndEnableAsync(manifest, slowScript);
        using var reachedLeasePoint = new ManualResetEventSlim();
        using var releaseLeasePoint = new ManualResetEventSlim();
        fixture.Manager.BeforeInvocationLeaseForTesting = () =>
        {
            fixture.Manager.BeforeInvocationLeaseForTesting = null;
            reachedLeasePoint.Set();
            releaseLeasePoint.Wait(TimeSpan.FromSeconds(5));
        };

        var invocation = Task.Run(() => fixture.InvokeAsync(manifest.Id, "slow"));
        Assert.IsTrue(reachedLeasePoint.Wait(TimeSpan.FromSeconds(2)));
        var uninstall = fixture.Manager.UninstallAsync(manifest.Id, deleteData: true, CancellationToken.None);
        await Task.Delay(100);
        Assert.IsFalse(uninstall.IsCompleted,
            "Uninstall must not pass the management gate before the invocation owns its lifecycle lease.");
        releaseLeasePoint.Set();
        await uninstall;
        await Assert.ThrowsExactlyAsync<PluginInvocationInterruptedException>(() => invocation);
        Assert.IsFalse(Directory.Exists(Path.Combine(
            fixture.RootPath, "packages", manifest.Id, manifest.Version)));
    }

    [TestMethod]
    public async Task Upgrade_WhenPostCatalogStepFails_RestoresOldCatalogPackageAndSnapshot()
    {
        const string versionOneScript = "globalThis.sdwPlugin={handlers:{version:()=>({value:1})}};";
        const string versionTwoScript = "globalThis.sdwPlugin={handlers:{version:()=>({value:2})}};";
        await using var fixture = new PluginPlatformFixture(enableFailureInjection: true);
        var versionOne = Manifest("test.rollback", version: "1.0.0");
        await fixture.InstallAndEnableAsync(versionOne, versionOneScript);
        fixture.FailingRepository!.FailNextRetainedRemoval = true;
        var versionTwo = versionOne with { Version = "2.0.0" };
        var preview = await fixture.PreviewAsync(versionTwo, versionTwoScript);

        await Assert.ThrowsExactlyAsync<IOException>(() => fixture.Manager.UpgradeAsync(
            versionOne.Id,
            preview.Token,
            preview.PackageSha256,
            preview.Manifest.Capabilities,
            CancellationToken.None));

        var installed = (await fixture.Manager.GetAllAsync(CancellationToken.None)).Single(plugin =>
            plugin.Manifest.Id == versionOne.Id);
        Assert.AreEqual("1.0.0", installed.Manifest.Version);
        Assert.IsTrue(installed.IsEnabled);
        var invoked = await fixture.InvokeAsync(versionOne.Id, "version");
        Assert.AreEqual(1, invoked.GetProperty("value").GetInt32());
        Assert.IsTrue(Directory.Exists(Path.Combine(
            fixture.RootPath, "packages", versionOne.Id, "1.0.0")));
        Assert.IsFalse(Directory.Exists(Path.Combine(
            fixture.RootPath, "packages", versionOne.Id, "2.0.0")));
    }

    [TestMethod]
    public async Task Uninstall_WhenCatalogRemovalFails_RestoresPackageDataCatalogAndSnapshot()
    {
        var (_, storageScript) = LoadExample("scoped-storage");
        await using var fixture = new PluginPlatformFixture(enableFailureInjection: true);
        var manifest = StorageManifest("test.uninstall-rollback");
        await fixture.InstallAndEnableAsync(manifest, storageScript);
        await fixture.InvokeAsync(manifest.Id, "seed", new
        {
            path = "state.txt",
            base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("preserve me"))
        });
        fixture.FailingRepository!.FailNextCatalogRemoval = true;

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            fixture.Manager.UninstallAsync(manifest.Id, deleteData: true, CancellationToken.None));

        var restored = (await fixture.Manager.GetAllAsync(CancellationToken.None)).Single(plugin =>
            plugin.Manifest.Id == manifest.Id);
        Assert.IsTrue(restored.IsEnabled);
        Assert.IsTrue(Directory.Exists(Path.Combine(
            fixture.RootPath, "packages", manifest.Id, manifest.Version)));
        var exists = await fixture.InvokeAsync(manifest.Id, "exists", new { path = "state.txt" });
        Assert.IsTrue(exists.GetProperty("exists").GetBoolean());
    }

    [TestMethod]
    public async Task UpgradeAndRetainedReinstall_RejectPublisherKeySubstitution()
    {
        using var originalKey = RSA.Create(2048);
        using var substituteKey = RSA.Create(2048);
        await using var fixture = new PluginPlatformFixture(options =>
        {
            options.AllowUnsignedLocalPackages = false;
            options.TrustedPublisherPublicKeys["original"] = originalKey.ExportSubjectPublicKeyInfoPem();
            options.TrustedPublisherPublicKeys["substitute"] = substituteKey.ExportSubjectPublicKeyInfoPem();
        });
        var versionOne = Sign(Manifest("test.publisher-owner"), PingScript, "original", originalKey);
        await fixture.InstallAndEnableAsync(versionOne, PingScript);
        var versionTwo = Sign(versionOne with { Version = "2.0.0", Signature = null }, PingScript,
            "substitute", substituteKey);
        var substituteUpgrade = await fixture.PreviewAsync(versionTwo, PingScript);

        var upgradeError = await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
            fixture.Manager.UpgradeAsync(versionOne.Id, substituteUpgrade.Token,
                substituteUpgrade.PackageSha256, substituteUpgrade.Manifest.Capabilities,
                CancellationToken.None));
        StringAssert.Contains(upgradeError.Message, "publisher identity");

        await fixture.Manager.UninstallAsync(versionOne.Id, deleteData: false, CancellationToken.None);
        var reinstallManifest = Sign(
            Manifest(versionOne.Id), PingScript, "substitute", substituteKey);
        var substituteReinstall = await fixture.PreviewAsync(reinstallManifest, PingScript);
        var reinstallError = await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
            fixture.Manager.InstallPackageAsync(substituteReinstall.Token,
                substituteReinstall.PackageSha256, substituteReinstall.Manifest.Capabilities,
                CancellationToken.None));
        StringAssert.Contains(reinstallError.Message, "retained owner");
    }

    private static PluginManifest Manifest(
        string id,
        string version = "1.0.0",
        string apiVersion = PluginApi.CurrentVersion,
        PluginCapabilities? capabilities = null)
        => new()
        {
            Id = id,
            Name = id,
            Version = version,
            ApiVersion = apiVersion,
            EntryPoint = "index.js",
            Capabilities = capabilities ?? new PluginCapabilities(),
            Integrity = new PluginIntegrity
            {
                Files = new Dictionary<string, string>
                {
                    ["index.js"] = new string('0', 64)
                }
            }
        };

    private static string GetSingleLifecycleTransaction(string rootPath, string operation, string pluginId)
    {
        var transactionRoot = Path.Combine(rootPath, "transactions");
        var transactions = Directory.Exists(transactionRoot)
            ? Directory.EnumerateDirectories(transactionRoot, $"{operation}-{pluginId}-*").ToArray()
            : [];
        Assert.HasCount(1, transactions);
        Assert.IsTrue(File.Exists($"{transactions[0]}.journal.json"));
        return transactions[0];
    }

    private static void AssertNoLifecycleTransactions(string rootPath)
    {
        var transactionRoot = Path.Combine(rootPath, "transactions");
        if (!Directory.Exists(transactionRoot)) return;
        Assert.IsEmpty(Directory.EnumerateDirectories(transactionRoot).ToArray());
        Assert.IsEmpty(Directory.EnumerateFiles(
            transactionRoot, "*.journal.json", SearchOption.TopDirectoryOnly).ToArray());
    }

    private static PluginManifest StorageManifest(string id, string? providerName = null)
        => Manifest(id, capabilities: new PluginCapabilities { StorageAccess = true }) with
        {
            Providers = [new PluginProviderDeclaration
            {
                Kind = "storage",
                Name = providerName ?? $"{id}-store",
                Handlers = new Dictionary<string, string>
                {
                    ["exists"] = "exists",
                    ["info"] = "info",
                    ["read"] = "read",
                    ["list"] = "list"
                }
            }]
        };

    private static (PluginManifest Manifest, string Script) LoadExample(string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Examples", name);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(
                           File.ReadAllText(Path.Combine(directory, "manifest.json")),
                           new JsonSerializerOptions(JsonSerializerDefaults.Web))
                       ?? throw new InvalidDataException($"Example manifest '{name}' is invalid.");
        return (manifest, File.ReadAllText(Path.Combine(directory, manifest.EntryPoint)));
    }

    private static PluginManifest WithIntegrity(
        PluginManifest manifest,
        string script,
        params (string Path, string Content)[] extraEntries)
        => manifest with
        {
            Integrity = new PluginIntegrity
            {
                Files = new[] { (manifest.EntryPoint, script) }
                    .Concat(extraEntries)
                    .ToDictionary(
                        entry => entry.Item1,
                        entry => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Item2)))
                            .ToLowerInvariant(),
                        StringComparer.Ordinal)
            }
        };

    private static PluginManifest Sign(
        PluginManifest manifest,
        string script,
        string publisher,
        RSA key,
        params (string Path, string Content)[] extraEntries)
    {
        manifest = WithIntegrity(manifest, script, extraEntries);
        var payload = PluginSignaturePayload.Create(manifest);
        return manifest with
        {
            Signature = new PluginSignature(
                publisher,
                "RSA-SHA256",
                Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)))
        };
    }

    private static MemoryStream BuildPackage(
        PluginManifest manifest,
        string script,
        params (string Path, string Content)[] extraEntries)
    {
        manifest = manifest.Integrity?.Files.TryGetValue(manifest.EntryPoint, out var entryDigest) != true ||
                   entryDigest == new string('0', 64)
            ? WithIntegrity(manifest, script, extraEntries)
            : manifest;
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            WriteEntry(archive, manifest.EntryPoint, script);
            foreach (var entry in extraEntries) WriteEntry(archive, entry.Path, entry.Content);
        }
        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private sealed class PluginPlatformFixture : IAsyncDisposable
    {
        private readonly IOptions<PluginPlatformOptions> _options;
        private readonly HttpMessageHandler _httpHandler;

        public PluginPlatformFixture(
            Action<PluginPlatformOptions>? configure = null,
            HttpMessageHandler? httpHandler = null,
            bool enableFailureInjection = false)
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"sdw-plugin-tests-{Guid.NewGuid():N}");
            var options = new PluginPlatformOptions
            {
                RootPath = RootPath,
                AllowUnsignedLocalPackages = true,
                InvocationTimeoutMilliseconds = 2_000,
                MaximumWorkerCpuMilliseconds = 1_500,
                MaximumWorkerMemoryMegabytes = 256,
                CircuitBreakerFailures = 3,
                CircuitBreakerSeconds = 60
            };
            configure?.Invoke(options);
            _options = Options.Create(options);
            _httpHandler = httpHandler ?? new RecordingHttpMessageHandler();
            IPluginCatalogRepository repository = new PluginCatalogRepository(_options);
            if (enableFailureInjection)
            {
                FailingRepository = new FailingPluginCatalogRepository(repository);
                repository = FailingRepository;
            }
            SafeFileAccess = new PluginSafeFileAccess();
            Manager = CreateManager(repository);
        }

        public string RootPath { get; }
        public PluginManager Manager { get; }
        public PluginSafeFileAccess SafeFileAccess { get; }
        public FailingPluginCatalogRepository? FailingRepository { get; }

        public PluginManager CreateRestartedManager()
            => CreateManager(new PluginCatalogRepository(_options));

        public async Task<PluginPackagePreview> PreviewAsync(PluginManifest manifest, string script)
        {
            await using var package = BuildPackage(manifest, script);
            return await Manager.PreviewPackageAsync(package, $"{manifest.Id}.sdwpkg", CancellationToken.None);
        }

        public async Task InstallAsync(PluginManifest manifest, string script)
        {
            var preview = await PreviewAsync(manifest, script);
            await Manager.InstallPackageAsync(preview.Token, preview.PackageSha256,
                preview.Manifest.Capabilities, CancellationToken.None);
        }

        public async Task InstallAndEnableAsync(PluginManifest manifest, string script)
        {
            await InstallAsync(manifest, script);
            await Manager.EnableAsync(manifest.Id, CancellationToken.None);
        }

        public Task<JsonElement> InvokeAsync(string id, string handler)
            => InvokeAsync(id, handler, new { });

        public Task<JsonElement> InvokeAsync(string id, string handler, object input)
            => Manager.InvokeAsync(id, handler, JsonSerializer.SerializeToElement(input), CancellationToken.None);

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
            return ValueTask.CompletedTask;
        }

        private PluginManager CreateManager(IPluginCatalogRepository repository)
        {
            var inspector = new PluginPackageInspector(_options);
            var broker = new PluginCapabilityBroker(
                new FixedHttpClientFactory(_httpHandler), _options, SafeFileAccess);
            var executor = new PluginProcessExecutor(broker, _options);
            return new PluginManager(repository, inspector, executor, _options, TimeProvider.System);
        }
    }

    private sealed class FailingPluginCatalogRepository(IPluginCatalogRepository inner)
        : IPluginCatalogRepository
    {
        public bool FailNextRetainedRemoval { get; set; }
        public bool FailNextCatalogRemoval { get; set; }

        public Task<IReadOnlyList<PluginCatalogEntry>> GetAllAsync(CancellationToken cancellationToken)
            => inner.GetAllAsync(cancellationToken);

        public Task<PluginCatalogEntry?> FindAsync(string id, CancellationToken cancellationToken)
            => inner.FindAsync(id, cancellationToken);

        public Task SaveAsync(PluginCatalogEntry entry, CancellationToken cancellationToken)
            => inner.SaveAsync(entry, cancellationToken);

        public Task RemoveAsync(string id, CancellationToken cancellationToken)
        {
            if (FailNextCatalogRemoval)
            {
                FailNextCatalogRemoval = false;
                throw new IOException("Injected catalog removal failure.");
            }
            return inner.RemoveAsync(id, cancellationToken);
        }

        public Task<RetainedPluginData?> FindRetainedAsync(string id, CancellationToken cancellationToken)
            => inner.FindRetainedAsync(id, cancellationToken);

        public Task SaveRetainedAsync(RetainedPluginData retained, CancellationToken cancellationToken)
            => inner.SaveRetainedAsync(retained, cancellationToken);

        public Task RemoveRetainedAsync(string id, CancellationToken cancellationToken)
        {
            if (FailNextRetainedRemoval)
            {
                FailNextRetainedRemoval = false;
                throw new IOException("Injected failure after new catalog write.");
            }
            return inner.RemoveRetainedAsync(id, cancellationToken);
        }
    }

    private sealed class FixedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FixedDnsResolver(params IPAddress[] addresses) : IPluginDnsResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.AreEqual("approved.example", host);
            return Task.FromResult(addresses);
        }
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent)
            {
                Content = new StringContent(string.Empty)
            };
        }
    }
}
