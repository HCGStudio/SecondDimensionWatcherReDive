using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SecondDimensionWatcherReDive.Exceptions;
using SecondDimensionWatcherReDive.Services;
using SecondDimensionWatcherReDive.Utils.Feed;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class TorrentBencodeComplexityTests
{
    [TestMethod]
    public void DepthBoundary_Allows64AndRejects65WithoutRecursion()
    {
        TorrentBencodeComplexityValidator.Validate(NestedList(TorrentBencodeComplexityValidator.MaximumDepth));

        Assert.ThrowsExactly<FormatException>(() =>
            TorrentBencodeComplexityValidator.Validate(
                NestedList(TorrentBencodeComplexityValidator.MaximumDepth + 1)));
    }

    [TestMethod]
    [DataRow("i-0e")]
    [DataRow("i01e")]
    [DataRow("01:a")]
    [DataRow("dli1eee")]
    [DataRow("i1ee")]
    [DataRow("l1:a")]
    public void StrictGrammarBoundary_RejectsMalformedDocument(string value)
    {
        Assert.ThrowsExactly<FormatException>(() =>
            TorrentBencodeComplexityValidator.Validate(Encoding.ASCII.GetBytes(value)));
    }

    [TestMethod]
    public void NodeEntryAndStringBounds_AreEnforcedBeforeParsing()
    {
        var excessiveEntries = Encoding.ASCII.GetBytes(
            "l" + string.Concat(Enumerable.Repeat("0:", TorrentBencodeComplexityValidator.MaximumEntries + 1)) + "e");

        Assert.ThrowsExactly<FormatException>(() =>
            TorrentBencodeComplexityValidator.Validate(excessiveEntries));
        Assert.ThrowsExactly<FormatException>(() =>
            TorrentBencodeComplexityValidator.Validate(Encoding.ASCII.GetBytes(
                $"{TorrentBencodeComplexityValidator.MaximumStringBytes + 1}:")));
    }

    [TestMethod]
    public void DictionaryKeys_MustBeUniqueAndStrictlyBytewiseIncreasing()
    {
        var duplicateKey = Encoding.ASCII.GetBytes(
            "d4:infod6:lengthi1e6:lengthi2eee");
        var outOfOrderKey = Encoding.ASCII.GetBytes(
            "d4:infod4:name1:a6:lengthi1eee");

        Assert.ThrowsExactly<FormatException>(() =>
            TorrentBencodeComplexityValidator.Validate(duplicateKey));
        Assert.ThrowsExactly<FormatException>(() =>
            TorrentBencodeComplexityValidator.Validate(outOfOrderKey));
        Assert.ThrowsExactly<InvalidTorrentDataException>(() =>
            SyncFeed.ParseTorrentData(duplicateKey, "https://example.com/file.torrent"));
    }

    [TestMethod]
    public void TorrentInfoHash_UsesTheValidatedOriginalInfoByteRange()
    {
        const string info = "d6:lengthi1e4:name1:ae";
        var torrent = Encoding.ASCII.GetBytes($"d4:info{info}e");

        var parsed = SyncFeed.ParseTorrentData(torrent, "https://example.com/file.torrent");
        var expected = Convert.ToHexString(SHA1.HashData(Encoding.ASCII.GetBytes(info)))
            .ToLowerInvariant();

        Assert.AreEqual(expected, parsed.Hash);
    }

    [TestMethod]
    public async Task DeepTorrent_IsRejectedInChildProcessWithoutKillingHost()
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", ".."));
        var applicationAssembly = Path.Combine(
            repositoryRoot,
            "SecondDimensionWatcherReDive.SecurityProbe",
            "bin",
            configuration,
            "net10.0",
            "SecondDimensionWatcherReDive.SecurityProbe.dll");
        Assert.IsTrue(File.Exists(applicationAssembly),
            $"The child probe assembly does not exist: {applicationAssembly}");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(applicationAssembly);
        foreach (var key in startInfo.Environment.Keys
                     .Where(IsCoverageEnvironmentVariable)
                     .ToArray())
            startInfo.Environment.Remove(key);

        using var process = Process.Start(startInfo)
            ?? throw new AssertFailedException("Could not start the child test process.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The deep-torrent child process did not terminate within 45 seconds.");
        }

        var output = await standardOutput;
        var error = await standardError;
        Assert.AreEqual(0, process.ExitCode, $"Child process failed.\n{output}\n{error}");
    }

    private static byte[] NestedList(int depth) => Encoding.ASCII.GetBytes(
        new string('l', depth) + "0:" + new string('e', depth));

    private static bool IsCoverageEnvironmentVariable(string key) =>
        key.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("COR_", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("COVERLET_", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("VSTEST_", StringComparison.OrdinalIgnoreCase);
}
