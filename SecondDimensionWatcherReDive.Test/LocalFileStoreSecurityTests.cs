using Microsoft.Extensions.Options;
using Moq;
using SecondDimensionWatcherReDive.Services;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class LocalFileStoreSecurityTests
{
    [TestMethod]
    public async Task OpenReadStreamAsync_OrdinaryFileInsideAllowedRoot_ReturnsContent()
    {
        using var sandbox = new TemporarySandbox();
        var filePath = Path.Combine(sandbox.AllowedRoot, "episode.mkv");
        var expected = new byte[] { 0x01, 0x23, 0x45, 0x67 };
        File.WriteAllBytes(filePath, expected);
        var store = CreateStore(sandbox.AllowedRoot);

        await using var stream = await store.OpenReadStreamAsync(
            filePath,
            CancellationToken.None);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        CollectionAssert.AreEqual(expected, buffer.ToArray());
        Assert.IsTrue(File.Exists(filePath));
        CollectionAssert.AreEqual(expected, File.ReadAllBytes(filePath));
    }

    [TestMethod]
    public async Task OpenReadStreamAsync_SymlinkInsideAllowedRootTargetsOutsideRoot_Throws()
    {
        using var sandbox = new TemporarySandbox();
        var outsideFile = Path.Combine(sandbox.OutsideRoot, "secret.mkv");
        var expected = new byte[] { 0x89, 0xab, 0xcd };
        File.WriteAllBytes(outsideFile, expected);
        var linkPath = Path.Combine(sandbox.AllowedRoot, "escaped.mkv");
        if (!TryCreateFileSymlink(linkPath, outsideFile, out var reason))
        {
            Assert.Inconclusive($"Symbolic links are unavailable in this environment: {reason}");
            return;
        }

        var store = CreateStore(sandbox.AllowedRoot);

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
            store.OpenReadStreamAsync(linkPath, CancellationToken.None));

        Assert.IsTrue(File.Exists(outsideFile));
        CollectionAssert.AreEqual(expected, File.ReadAllBytes(outsideFile));
    }

    [TestMethod]
    public async Task OpenReadStreamAsync_DownloadRootIsSymlinkAlias_OpensAliasAndResolvedPaths()
    {
        using var sandbox = new TemporarySandbox();
        var realDownloadRoot = Directory.CreateDirectory(
            Path.Combine(sandbox.OutsideRoot, "managed-downloads")).FullName;
        var aliasRoot = Path.Combine(sandbox.RootPath, "download-alias");
        if (!TryCreateDirectorySymlink(aliasRoot, realDownloadRoot, out var reason))
        {
            Assert.Inconclusive($"Symbolic links are unavailable in this environment: {reason}");
            return;
        }

        var expected = new byte[] { 0xde, 0xad, 0xbe, 0xef };
        var resolvedFilePath = Path.Combine(realDownloadRoot, "ordinary-download.mkv");
        var aliasFilePath = Path.Combine(aliasRoot, "ordinary-download.mkv");
        File.WriteAllBytes(resolvedFilePath, expected);
        var store = CreateStore([], aliasRoot);

        CollectionAssert.AreEqual(expected, await ReadAllAsync(store, aliasFilePath));
        CollectionAssert.AreEqual(expected, await ReadAllAsync(store, resolvedFilePath));
        Assert.IsTrue(File.Exists(resolvedFilePath));
        CollectionAssert.AreEqual(expected, File.ReadAllBytes(resolvedFilePath));
    }

    private static LocalFileStore CreateStore(string allowedRoot)
        => CreateStore([allowedRoot], downloadRoot: null);

    private static LocalFileStore CreateStore(
        string[] allowedRoots,
        string? downloadRoot)
    {
        var options = new Mock<IOptionsMonitor<MediaLibraryOptions>>();
        options.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new MediaLibraryOptions
            {
                AllowedRoots = allowedRoots,
                DownloadRoot = downloadRoot
            });
        return new LocalFileStore(options.Object);
    }

    private static async Task<byte[]> ReadAllAsync(LocalFileStore store, string path)
    {
        await using var stream = await store.OpenReadStreamAsync(path, CancellationToken.None);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static bool TryCreateFileSymlink(
        string linkPath,
        string targetPath,
        out string reason)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or PlatformNotSupportedException
                                          or NotSupportedException)
        {
            reason = exception.Message;
            return false;
        }
    }

    private static bool TryCreateDirectorySymlink(
        string linkPath,
        string targetPath,
        out string reason)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or PlatformNotSupportedException
                                          or NotSupportedException)
        {
            reason = exception.Message;
            return false;
        }
    }

    private sealed class TemporarySandbox : IDisposable
    {
        public TemporarySandbox()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                $"sdw-local-store-security-tests-{Guid.NewGuid():N}");
            AllowedRoot = Directory.CreateDirectory(
                Path.Combine(RootPath, "allowed")).FullName;
            OutsideRoot = Directory.CreateDirectory(
                Path.Combine(RootPath, "outside")).FullName;
        }

        public string RootPath { get; }
        public string AllowedRoot { get; }
        public string OutsideRoot { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
        }
    }
}
