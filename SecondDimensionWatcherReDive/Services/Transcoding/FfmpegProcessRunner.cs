using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace SecondDimensionWatcherReDive.Services.Transcoding;

internal sealed record FfmpegProgress(double? ProcessedSeconds, double? Speed, bool FirstSegmentReady);

internal sealed record FfmpegRunResult(int ExitCode, string ErrorOutput);

internal interface IFfmpegProcessRunner
{
    Task<MediaProbe> ProbeAsync(Stream source, CancellationToken cancellationToken);

    Task<FfmpegRunResult> GenerateHlsAsync(
        Stream source,
        TranscodingPlan plan,
        TranscodingSelection selection,
        string outputDirectory,
        bool useHardwareEncoder,
        Action<FfmpegProgress> onProgress,
        CancellationToken cancellationToken);

    Task<TranscodingSubtitle?> ExtractTextSubtitleAsync(
        Stream source,
        MediaStreamProbe subtitle,
        int ordinal,
        string outputDirectory,
        CancellationToken cancellationToken);
}

internal sealed partial class FfmpegProcessRunner(
    IOptions<TranscodingOptions> options,
    ILogger<FfmpegProcessRunner> logger) : IFfmpegProcessRunner
{
    private readonly TranscodingOptions _options = options.Value;
    private static readonly JsonSerializerOptions ProbeJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MediaProbe> ProbeAsync(Stream source, CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(_options.FfprobePath, redirectOutput: true);
        AddArguments(startInfo,
            "-v", "error",
            "-analyzeduration", "10000000",
            "-probesize", "10000000",
            "-read_intervals", "%+#32",
            "-show_format",
            "-show_streams",
            "-of", "json",
            "-i", "pipe:0");

        using var process = Start(startInfo);
        var pumpTask = PumpInputAsync(process, source, cancellationToken);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            Kill(process);
            await WaitForExitIgnoringErrorsAsync(process);
            await IgnoreBrokenPipeAsync(pumpTask, cancellationToken);
            await Task.WhenAll(outputTask, errorTask);
            throw;
        }

        await IgnoreBrokenPipeAsync(pumpTask, cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"ffprobe exited with code {process.ExitCode}: {TrimError(error)}");

        var document = JsonSerializer.Deserialize<FfprobeDocument>(output, ProbeJsonOptions)
                       ?? throw new InvalidOperationException("ffprobe returned an empty response.");
        var streams = (document.Streams ?? [])
            .Where(stream => stream.Index is not null && !string.IsNullOrWhiteSpace(stream.CodecType))
            .Select(stream => new MediaStreamProbe(
                stream.Index!.Value,
                stream.CodecType!.Trim().ToLowerInvariant(),
                string.IsNullOrWhiteSpace(stream.CodecName)
                    ? "unknown"
                    : stream.CodecName.Trim().ToLowerInvariant(),
                stream.Tags?.Language,
                stream.Tags?.Title,
                stream.Disposition?.Default == 1,
                stream.Disposition?.Forced == 1,
                stream.Disposition?.AttachedPic == 1,
                stream.Profile,
                stream.PixelFormat))
            .ToArray();
        var duration = ParseDuration(document.Format?.Duration)
                       ?? (document.Streams ?? []).Select(stream => ParseDuration(stream.Duration)).FirstOrDefault(value => value is not null);
        return new MediaProbe(document.Format?.FormatName ?? "unknown", duration, streams);
    }

    public async Task<FfmpegRunResult> GenerateHlsAsync(
        Stream source,
        TranscodingPlan plan,
        TranscodingSelection selection,
        string outputDirectory,
        bool useHardwareEncoder,
        Action<FfmpegProgress> onProgress,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(_options.FfmpegPath, redirectOutput: false);
        AddArguments(startInfo, "-hide_banner", "-y");
        if (useHardwareEncoder)
            foreach (var argument in _options.HardwareInputArguments) startInfo.ArgumentList.Add(argument);
        AddArguments(startInfo, "-i", "pipe:0");
        if (plan.BitmapSubtitleToBurn is null)
            AddArguments(startInfo, "-map", $"0:{plan.Video.Index}");

        if (plan.BitmapSubtitleToBurn is not null)
        {
            var maximumHeight = selection.Quality switch
            {
                "720p" => 720,
                "1080p" => 1080,
                _ => 0
            };
            var filter = $"[0:{plan.Video.Index}][0:{plan.BitmapSubtitleToBurn.Index}]overlay";
            if (maximumHeight > 0) filter += $",scale=-2:min({maximumHeight}\\,ih)";
            filter += "[vout]";
            AddArguments(startInfo,
                "-filter_complex",
                filter,
                "-map", "[vout]");
        }
        if (plan.Audio is not null) AddArguments(startInfo, "-map", $"0:{plan.Audio.Index}");

        if (plan.CopyVideo)
        {
            AddArguments(startInfo, "-c:v", "copy");
        }
        else
        {
            AddArguments(startInfo,
                "-c:v", useHardwareEncoder ? _options.HardwareVideoEncoder! : "libx264");
            if (!useHardwareEncoder)
                AddArguments(startInfo, "-preset", _options.VideoPreset, "-crf", _options.VideoCrf.ToString(CultureInfo.InvariantCulture));
            AddArguments(startInfo, "-pix_fmt", "yuv420p");
            var maximumHeight = selection.Quality switch
            {
                "720p" => 720,
                "1080p" => 1080,
                _ => 0
            };
            if (maximumHeight > 0 && plan.BitmapSubtitleToBurn is null)
                AddArguments(startInfo, "-vf", $"scale=-2:min({maximumHeight}\\,ih)");
            AddArguments(startInfo,
                "-force_key_frames",
                $"expr:gte(t,n_forced*{_options.SegmentDurationSeconds})");
        }

        if (plan.Audio is not null)
        {
            if (plan.CopyAudio) AddArguments(startInfo, "-c:a", "copy");
            else AddArguments(startInfo, "-c:a", "aac", "-b:a", "192k", "-ac", "2");
        }

        var playlistPath = Path.Combine(outputDirectory, "media.m3u8");
        var segmentPattern = Path.Combine(outputDirectory, "segment-%06d.ts");
        AddArguments(startInfo,
            "-sn",
            "-threads", _options.MaxThreadsPerJob.ToString(CultureInfo.InvariantCulture),
            "-max_muxing_queue_size", "1024",
            "-f", "hls",
            "-hls_time", _options.SegmentDurationSeconds.ToString(CultureInfo.InvariantCulture),
            "-hls_list_size", "0",
            "-hls_playlist_type", "event",
            "-hls_flags", "independent_segments+temp_file",
            "-hls_segment_filename", segmentPattern,
            "-progress", "pipe:2",
            "-nostats",
            playlistPath);

        return await RunFfmpegAsync(
            startInfo,
            source,
            outputDirectory,
            onProgress,
            cancellationToken);
    }

    public async Task<TranscodingSubtitle?> ExtractTextSubtitleAsync(
        Stream source,
        MediaStreamProbe subtitle,
        int ordinal,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var finalPath = Path.Combine(outputDirectory, $"subtitle-{subtitle.Index}.vtt");
        var temporaryPath = $"{finalPath}.tmp";
        TryDelete(temporaryPath);
        var startInfo = CreateStartInfo(_options.FfmpegPath, redirectOutput: false);
        AddArguments(startInfo,
            "-hide_banner", "-y",
            "-i", "pipe:0",
            "-threads", _options.MaxThreadsPerJob.ToString(CultureInfo.InvariantCulture),
            "-nostats",
            "-map", $"0:{subtitle.Index}",
            "-c:s", "webvtt",
            "-f", "webvtt",
            temporaryPath);
        var result = await RunFfmpegAsync(
            startInfo,
            source,
            outputDirectory,
            _ => { },
            cancellationToken,
            detectFirstSegment: false);
        if (result.ExitCode != 0)
        {
            LogSubtitleExtractionFailed(logger, subtitle.Index, result.ExitCode, result.ErrorOutput);
            TryDelete(temporaryPath);
            return null;
        }

        if (!File.Exists(temporaryPath)) return null;
        File.Move(temporaryPath, finalPath, overwrite: true);
        return new TranscodingSubtitle(
            Path.GetFileName(finalPath),
            BuildSubtitleLabel(subtitle, ordinal),
            subtitle.Language,
            "vtt");
    }

    private async Task<FfmpegRunResult> RunFfmpegAsync(
        ProcessStartInfo startInfo,
        Stream source,
        string outputDirectory,
        Action<FfmpegProgress> onProgress,
        CancellationToken cancellationToken,
        bool detectFirstSegment = true)
    {
        using var process = Start(startInfo);
        var recentErrors = new Queue<string>();
        var errorGate = new object();
        double? lastSpeed = null;
        double? lastProcessedSeconds = null;
        var firstSegmentReady = 0;
        string? resourceViolation = null;
        var pumpTask = PumpInputAsync(process, source, cancellationToken);
        var errorTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
            {
                if (TryParseProgress(line, out var processedSeconds, out var speed))
                {
                    if (processedSeconds is not null) lastProcessedSeconds = processedSeconds;
                    if (speed is not null) lastSpeed = speed;
                    onProgress(new FfmpegProgress(
                        lastProcessedSeconds,
                        lastSpeed,
                        Volatile.Read(ref firstSegmentReady) == 1));
                }
                lock (errorGate)
                {
                    recentErrors.Enqueue(line);
                    while (recentErrors.Count > 20) recentErrors.Dequeue();
                }
            }
        }, CancellationToken.None);
        var monitorTask = Task.Run(async () =>
        {
            while (!process.HasExited)
            {
                await Task.Delay(250, cancellationToken);
                if (_options.MaxMemoryBytesPerJob > 0
                    && TryGetWorkingSet(process, out var workingSet)
                    && workingSet > _options.MaxMemoryBytesPerJob)
                {
                    resourceViolation = $"FFmpeg exceeded its {_options.MaxMemoryBytesPerJob} byte memory limit.";
                    Kill(process);
                    return;
                }

                if (_options.MaxDiskBytesPerJob > 0
                    && GetDirectorySize(outputDirectory) > _options.MaxDiskBytesPerJob)
                {
                    resourceViolation = $"FFmpeg exceeded its {_options.MaxDiskBytesPerJob} byte disk limit.";
                    Kill(process);
                    return;
                }

                if (detectFirstSegment
                    && Volatile.Read(ref firstSegmentReady) == 0
                    && HasPlayableSegment(outputDirectory)
                    && Interlocked.Exchange(ref firstSegmentReady, 1) == 0)
                {
                    onProgress(new FfmpegProgress(lastProcessedSeconds, lastSpeed, true));
                }
            }
        }, CancellationToken.None);

        Exception? waitException = null;
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            Kill(process);
            await WaitForExitIgnoringErrorsAsync(process);
            waitException = exception;
        }
        finally
        {
            await IgnoreBrokenPipeAsync(pumpTask, cancellationToken);
        }

        try { await errorTask; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        try { await monitorTask; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        if (waitException is not null) ExceptionDispatchInfo.Capture(waitException).Throw();
        if (resourceViolation is not null) throw new TranscodingResourceLimitException(resourceViolation);
        if (detectFirstSegment
            && Volatile.Read(ref firstSegmentReady) == 0
            && HasPlayableSegment(outputDirectory)
            && Interlocked.Exchange(ref firstSegmentReady, 1) == 0)
            onProgress(new FfmpegProgress(lastProcessedSeconds, lastSpeed, true));
        string errors;
        lock (errorGate) errors = string.Join(" | ", recentErrors);
        return new FfmpegRunResult(process.ExitCode, TrimError(errors));
    }

    private static ProcessStartInfo CreateStartInfo(string path, bool redirectOutput)
        => new()
        {
            FileName = path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = true
        };

    private static Process Start(ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo)
                   ?? throw new InvalidOperationException($"Unable to start {startInfo.FileName}.");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Unable to start {startInfo.FileName}. Install FFmpeg or update Transcoding paths.",
                exception);
        }
    }

    private static void AddArguments(ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
    }

    private static async Task PumpInputAsync(
        Process process,
        Stream source,
        CancellationToken cancellationToken)
    {
        try
        {
            await source.CopyToAsync(process.StandardInput.BaseStream, cancellationToken);
        }
        finally
        {
            try { process.StandardInput.Close(); }
            catch (IOException) { }
        }
    }

    private static async Task IgnoreBrokenPipeAsync(Task pumpTask, CancellationToken cancellationToken)
    {
        try { await pumpTask; }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private static bool TryParseProgress(
        string line,
        out double? processedSeconds,
        out double? speed)
    {
        processedSeconds = null;
        speed = null;
        var separator = line.IndexOf('=');
        if (separator <= 0) return false;
        var key = line[..separator];
        var value = line[(separator + 1)..];
        if (key is "out_time_us" or "out_time_ms")
        {
            // Current FFmpeg reports microseconds for both legacy out_time_ms and out_time_us.
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
            {
                processedSeconds = Math.Max(0, microseconds / 1_000_000d);
                return true;
            }
        }
        else if (key == "speed")
        {
            var normalized = value.TrimEnd('x');
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                speed = parsed;
            return true;
        }
        return key == "progress";
    }

    public static double? ToProgressFraction(double? processedSeconds, TimeSpan? duration)
    {
        if (processedSeconds is null || duration is null || duration.Value.TotalSeconds <= 0) return null;
        return Math.Clamp(processedSeconds.Value / duration.Value.TotalSeconds, 0, 1);
    }

    private static TimeSpan? ParseDuration(string? value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
           && double.IsFinite(seconds)
           && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;

    private static bool TryGetWorkingSet(Process process, out long workingSet)
    {
        try
        {
            process.Refresh();
            workingSet = process.WorkingSet64;
            return true;
        }
        catch (InvalidOperationException)
        {
            workingSet = 0;
            return false;
        }
    }

    private static long GetDirectorySize(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file =>
                {
                    try { return new FileInfo(file).Length; }
                    catch (IOException) { return 0; }
                });
        }
        catch (DirectoryNotFoundException)
        {
            return 0;
        }
    }

    private static bool HasPlayableSegment(string outputDirectory)
    {
        var playlist = Path.Combine(outputDirectory, "media.m3u8");
        if (!File.Exists(playlist)) return false;
        try
        {
            return Directory.EnumerateFiles(outputDirectory, "segment-*.ts")
                .Any(path => new FileInfo(path).Length > 0);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
    }

    private static async Task WaitForExitIgnoringErrorsAsync(Process process)
    {
        try { await process.WaitForExitAsync(CancellationToken.None); }
        catch (InvalidOperationException) { }
    }

    private static string BuildSubtitleLabel(MediaStreamProbe stream, int ordinal)
    {
        if (!string.IsNullOrWhiteSpace(stream.Title)) return stream.Title;
        if (!string.IsNullOrWhiteSpace(stream.Language)) return $"{stream.Language.ToUpperInvariant()} · Embedded";
        return $"Embedded subtitle {ordinal}";
    }

    private static string TrimError(string error)
        => error.Length <= 4000 ? error : error[^4000..];

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "FFmpeg subtitle stream {StreamIndex} extraction exited with code {ExitCode}: {Error}")]
    private static partial void LogSubtitleExtractionFailed(
        ILogger logger,
        int streamIndex,
        int exitCode,
        string error);

    private sealed record FfprobeDocument(
        [property: JsonPropertyName("streams")] FfprobeStream[]? Streams,
        [property: JsonPropertyName("format")] FfprobeFormat? Format);

    private sealed record FfprobeStream(
        [property: JsonPropertyName("index")] int? Index,
        [property: JsonPropertyName("codec_name")] string? CodecName,
        [property: JsonPropertyName("codec_type")] string? CodecType,
        [property: JsonPropertyName("profile")] string? Profile,
        [property: JsonPropertyName("pix_fmt")] string? PixelFormat,
        [property: JsonPropertyName("duration")] string? Duration,
        [property: JsonPropertyName("disposition")] FfprobeDisposition? Disposition,
        [property: JsonPropertyName("tags")] FfprobeTags? Tags);

    private sealed record FfprobeDisposition(
        [property: JsonPropertyName("default")] int Default,
        [property: JsonPropertyName("forced")] int Forced,
        [property: JsonPropertyName("attached_pic")] int AttachedPic);

    private sealed record FfprobeTags(
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("title")] string? Title);

    private sealed record FfprobeFormat(
        [property: JsonPropertyName("format_name")] string? FormatName,
        [property: JsonPropertyName("duration")] string? Duration);
}
