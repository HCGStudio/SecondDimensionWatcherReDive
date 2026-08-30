using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Services.Transcoding;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class FfmpegProcessRunnerTests
{
    [TestMethod]
    public async Task ProbeAndGenerateHlsAsync_ProducesProgressivePlaylistFromPipeInput()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sdw-ffmpeg-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "sample.mkv");
            await CreateSampleAsync(sourcePath, CancellationToken.None);
            var options = Options.Create(new TranscodingOptions
            {
                FfmpegPath = "ffmpeg",
                FfprobePath = "ffprobe",
                MaxThreadsPerJob = 1,
                SegmentDurationSeconds = 2,
                MaxMemoryBytesPerJob = 1024L * 1024 * 1024,
                MaxDiskBytesPerJob = 64L * 1024 * 1024
            });
            var runner = new FfmpegProcessRunner(
                options,
                NullLogger<FfmpegProcessRunner>.Instance);
            MediaProbe probe;
            await using (var source = File.OpenRead(sourcePath))
                probe = await runner.ProbeAsync(source, CancellationToken.None);
            Assert.IsTrue(probe.Video?.Profile is "Baseline" or "Constrained Baseline" or "Main" or "High");
            Assert.AreEqual("yuv420p", probe.Video?.PixelFormat);
            var sourceInfo = new FileInfo(sourcePath);
            var sourceModel = new TranscodingSource(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "/Anime/Group/sample.mkv",
                sourcePath,
                "test",
                sourceInfo.Name,
                sourceInfo.Length,
                sourceInfo.LastWriteTimeUtc);
            var selection = TranscodingSelection.Create("auto", null, null, null, null);
            var plan = TranscodingPlanner.CreatePlan(sourceModel, probe, selection, false);
            Assert.AreEqual(TranscodingStrategy.Remux, plan.Strategy);

            var output = Path.Combine(root, "hls");
            Directory.CreateDirectory(output);
            var updates = new List<FfmpegProgress>();
            FfmpegRunResult result;
            await using (var source = File.OpenRead(sourcePath))
                result = await runner.GenerateHlsAsync(
                    source,
                    plan,
                    selection,
                    output,
                    useHardwareEncoder: false,
                    update => updates.Add(update),
                    CancellationToken.None);

            Assert.AreEqual(0, result.ExitCode, result.ErrorOutput);
            Assert.IsTrue(File.Exists(Path.Combine(output, "media.m3u8")));
            Assert.IsTrue(Directory.EnumerateFiles(output, "segment-*.ts").Any());
            Assert.IsTrue(updates.Any(update => update.FirstSegmentReady));
            StringAssert.Contains(
                await File.ReadAllTextAsync(Path.Combine(output, "media.m3u8")),
                "#EXT-X-ENDLIST");
            var subtitles = new List<TranscodingSubtitle>();
            for (var index = 0; index < plan.TextSubtitles.Count; index++)
            {
                await using var source = File.OpenRead(sourcePath);
                var subtitle = await runner.ExtractTextSubtitleAsync(
                    source,
                    plan.TextSubtitles[index],
                    index + 1,
                    output,
                    CancellationToken.None);
                if (subtitle is not null) subtitles.Add(subtitle);
            }
            Assert.AreEqual(1, subtitles.Count);
            StringAssert.StartsWith(
                await File.ReadAllTextAsync(Path.Combine(output, subtitles[0].FileName)),
                "WEBVTT");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CreateSampleAsync(string path, CancellationToken cancellationToken)
    {
        var subtitlePath = Path.ChangeExtension(path, ".srt");
        await File.WriteAllTextAsync(
            subtitlePath,
            "1\n00:00:00,000 --> 00:00:01,000\nHello from SDW\n",
            cancellationToken);
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
                 {
                     "-hide_banner", "-loglevel", "error", "-y",
                     "-f", "lavfi", "-i", "testsrc=size=160x90:rate=10",
                     "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000",
                     "-f", "srt", "-i", subtitlePath,
                     "-t", "2",
                     "-map", "0:v:0", "-map", "1:a:0", "-map", "2:s:0",
                     "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p",
                     "-c:a", "aac", "-b:a", "64k",
                     "-c:s", "srt",
                     "-f", "matroska", path
                 })
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Unable to start FFmpeg test fixture generation.");
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var error = await errorTask;
        await outputTask;
        Assert.AreEqual(0, process.ExitCode, error);
    }
}
