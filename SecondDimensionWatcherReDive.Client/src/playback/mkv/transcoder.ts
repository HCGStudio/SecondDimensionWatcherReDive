import { FFFSType, FFmpeg } from "@ffmpeg/ffmpeg";
import ffmpegCoreUrl from "url:@ffmpeg/core";
import ffmpegWasmUrl from "url:@ffmpeg/core/wasm";

export type MkvTranscodeStage =
  | "loadingTranscoder"
  | "downloading"
  | "readingTracks"
  | "convertingSubtitles"
  | "transcodingVideo"
  | "finalizing";

export interface MkvTranscodeUpdate {
  stage: MkvTranscodeStage;
  progress?: number;
}

export interface TranscodedMkvSubtitle {
  path: string;
  virtualPath: string;
  language: string | null;
  label: string;
  format: "vtt";
  url: string;
}

export interface TranscodedMkvResult {
  url: string;
  videoCodec: string;
  audioCodec: string | null;
  subtitles: TranscodedMkvSubtitle[];
  skippedSubtitleCount: number;
  release: () => void;
}

export interface MkvTranscodeOptions {
  /** Preserve a browser-decodable video stream when only audio needs conversion. */
  copyVideo?: boolean;
}

interface ProbeStream {
  index: number;
  codec_name?: string;
  codec_type?: "video" | "audio" | "subtitle" | string;
  disposition?: {
    attached_pic?: number;
    default?: number;
    forced?: number;
  };
  tags?: {
    language?: string;
    title?: string;
  };
}

interface ProbeResult {
  streams?: ProbeStream[];
}

interface ExtractTextSubtitlesResult {
  subtitles: TranscodedMkvSubtitle[];
  skippedCount: number;
}

const TEXT_SUBTITLE_CODECS = new Set([
  "ass",
  "jacosub",
  "microdvd",
  "mov_text",
  "mpl2",
  "realtext",
  "sami",
  "ssa",
  "subrip",
  "subviewer",
  "subviewer1",
  "text",
  "vplayer",
  "webvtt",
]);

const abortError = (): DOMException =>
  new DOMException("The operation was aborted", "AbortError");

const clampProgress = (value: number): number =>
  Math.min(1, Math.max(0, Number.isFinite(value) ? value : 0));

const uint8ArrayBuffer = (value: Uint8Array): ArrayBuffer =>
  value.buffer.slice(
    value.byteOffset,
    value.byteOffset + value.byteLength,
  ) as ArrayBuffer;

const fetchBlobWithProgress = async (
  url: string,
  signal: AbortSignal,
  onProgress: (progress?: number) => void,
): Promise<Blob> => {
  const response = await fetch(url, { signal });
  if (!response.ok) {
    throw new Error(`Unable to download MKV (${response.status})`);
  }

  const total = Number(response.headers.get("content-length"));
  if (!response.body) {
    const blob = await response.blob();
    onProgress(1);
    return blob;
  }

  const reader = response.body.getReader();
  let loaded = 0;
  const stream = new ReadableStream<Uint8Array>({
    async pull(controller) {
      if (signal.aborted) {
        await reader.cancel();
        controller.error(abortError());
        return;
      }
      const { done, value } = await reader.read();
      if (done) {
        controller.close();
        onProgress(1);
        return;
      }
      loaded += value.byteLength;
      onProgress(
        Number.isFinite(total) && total > 0 ? loaded / total : undefined,
      );
      controller.enqueue(value);
    },
    cancel(reason) {
      return reader.cancel(reason);
    },
  });

  return await new Response(stream, {
    headers: {
      "Content-Type":
        response.headers.get("content-type") ?? "video/x-matroska",
    },
  }).blob();
};

const subtitleLabel = (stream: ProbeStream, ordinal: number): string => {
  const title = stream.tags?.title?.trim();
  const language = stream.tags?.language?.trim();
  if (title) return title;
  if (language) return `${language.toUpperCase()} · Embedded`;
  return `Embedded subtitle ${ordinal}`;
};

const extractTextSubtitles = async (
  ffmpeg: FFmpeg,
  inputPath: string,
  streams: ProbeStream[],
  signal: AbortSignal,
): Promise<ExtractTextSubtitlesResult> => {
  const subtitles: TranscodedMkvSubtitle[] = [];
  let skippedCount = 0;

  // Convert tracks independently. A malformed or nominally text subtitle must
  // not prevent the audio/video fallback from producing playable media.
  for (const [ordinal, stream] of streams.entries()) {
    const outputPath = `/subtitle-${stream.index}.vtt`;
    try {
      const exitCode = await ffmpeg.exec(
        [
          "-i",
          inputPath,
          "-map",
          `0:${stream.index}`,
          "-c:s",
          "webvtt",
          outputPath,
        ],
        -1,
        { signal },
      );
      if (exitCode !== 0) {
        skippedCount += 1;
        continue;
      }

      const data = await ffmpeg.readFile(outputPath, undefined, { signal });
      if (typeof data === "string") {
        skippedCount += 1;
        continue;
      }

      const url = URL.createObjectURL(
        new Blob([uint8ArrayBuffer(data)], { type: "text/vtt;charset=utf-8" }),
      );
      subtitles.push({
        path: `__mkv_subtitle_${stream.index}`,
        virtualPath: `mkv://subtitle/${stream.index}`,
        language: stream.tags?.language ?? null,
        label: subtitleLabel(stream, ordinal + 1),
        format: "vtt",
        url,
      });
    } catch {
      if (signal.aborted) throw abortError();
      skippedCount += 1;
    }
  }

  return { subtitles, skippedCount };
};

/**
 * Last-resort software conversion for codecs that WebCodecs cannot decode.
 * WORKERFS avoids copying the input into the WebAssembly heap; the output is
 * still materialized as a Blob because native playback needs a seekable file.
 */
export const transcodeMkvForBrowser = async (
  sourceUrl: string,
  signal: AbortSignal,
  onUpdate: (update: MkvTranscodeUpdate) => void,
  options: MkvTranscodeOptions = {},
): Promise<TranscodedMkvResult> => {
  if (signal.aborted) throw abortError();

  const ffmpeg = new FFmpeg();
  const createdUrls: string[] = [];
  const recentLogs: string[] = [];
  const logListener = ({ message }: { message: string }) => {
    recentLogs.push(message);
    if (recentLogs.length > 8) recentLogs.shift();
  };
  const conversionError = (message: string): Error =>
    new Error(
      recentLogs.length > 0 ? `${message}: ${recentLogs.join(" | ")}` : message,
    );
  ffmpeg.on("log", logListener);
  let mounted = false;
  const onAbort = () => ffmpeg.terminate();
  signal.addEventListener("abort", onAbort, { once: true });

  try {
    onUpdate({ stage: "loadingTranscoder" });
    await ffmpeg.load(
      { coreURL: ffmpegCoreUrl, wasmURL: ffmpegWasmUrl },
      { signal },
    );

    onUpdate({ stage: "downloading", progress: 0 });
    const sourceBlob = await fetchBlobWithProgress(
      sourceUrl,
      signal,
      (progress) => onUpdate({ stage: "downloading", progress }),
    );

    await ffmpeg.createDir("/source", { signal });
    mounted = await ffmpeg.mount(
      FFFSType.WORKERFS,
      { blobs: [{ name: "episode.mkv", data: sourceBlob }] },
      "/source",
    );
    if (!mounted) {
      throw conversionError("Unable to mount the MKV in FFmpeg");
    }
    const inputPath = "/source/episode.mkv";

    onUpdate({ stage: "readingTracks" });
    const probePath = "/probe.json";
    const probeExitCode = await ffmpeg.ffprobe(
      [
        "-v",
        "error",
        "-show_streams",
        "-of",
        "json",
        inputPath,
        "-o",
        probePath,
      ],
      -1,
      { signal },
    );
    let probeData: string | Uint8Array;
    try {
      probeData = await ffmpeg.readFile(probePath, "utf8", { signal });
    } catch {
      throw conversionError(
        `Unable to inspect MKV tracks (exit ${probeExitCode})`,
      );
    }
    const probe = JSON.parse(String(probeData)) as ProbeResult;
    const streams = probe.streams ?? [];
    const videoStream = streams.find(
      (stream) =>
        stream.codec_type === "video" && stream.disposition?.attached_pic !== 1,
    );
    if (!videoStream) throw new Error("The MKV file has no video track");
    const audioStream = streams.find((stream) => stream.codec_type === "audio");
    const subtitleStreams = streams.filter(
      (stream) => stream.codec_type === "subtitle",
    );
    const textSubtitleStreams = subtitleStreams.filter((stream) =>
      TEXT_SUBTITLE_CODECS.has(stream.codec_name?.toLowerCase() ?? ""),
    );

    onUpdate({ stage: "convertingSubtitles" });
    const subtitleExtraction = await extractTextSubtitles(
      ffmpeg,
      inputPath,
      textSubtitleStreams,
      signal,
    );
    const subtitles = subtitleExtraction.subtitles;
    createdUrls.push(...subtitles.map((subtitle) => subtitle.url));

    onUpdate({ stage: "transcodingVideo", progress: 0 });
    recentLogs.length = 0;
    const progressListener = ({ progress }: { progress: number }) => {
      onUpdate({
        stage: "transcodingVideo",
        progress: clampProgress(progress),
      });
    };
    ffmpeg.on("progress", progressListener);
    const outputPath = "/episode-browser.mp4";
    const transcodeExitCode = await ffmpeg.exec(
      [
        "-i",
        inputPath,
        "-map",
        `0:${videoStream.index}`,
        ...(audioStream ? ["-map", `0:${audioStream.index}`] : []),
        "-sn",
        ...(options.copyVideo
          ? ["-c:v", "copy"]
          : [
              "-c:v",
              "libx264",
              "-preset",
              "ultrafast",
              "-crf",
              "23",
              "-pix_fmt",
              "yuv420p",
            ]),
        ...(audioStream ? ["-c:a", "aac", "-b:a", "192k", "-ac", "2"] : []),
        "-movflags",
        "+faststart",
        "-max_muxing_queue_size",
        "1024",
        outputPath,
      ],
      -1,
      { signal },
    );
    ffmpeg.off("progress", progressListener);
    if (transcodeExitCode !== 0) {
      throw conversionError("Unable to convert the MKV video stream");
    }

    onUpdate({ stage: "finalizing" });
    const outputData = await ffmpeg.readFile(outputPath, undefined, { signal });
    if (typeof outputData === "string") {
      throw new Error("FFmpeg returned an invalid video payload");
    }
    const videoUrl = URL.createObjectURL(
      new Blob([uint8ArrayBuffer(outputData)], { type: "video/mp4" }),
    );
    createdUrls.push(videoUrl);

    let released = false;
    return {
      url: videoUrl,
      videoCodec: videoStream.codec_name ?? "unknown",
      audioCodec: audioStream?.codec_name ?? null,
      subtitles,
      skippedSubtitleCount:
        subtitleStreams.length -
        textSubtitleStreams.length +
        subtitleExtraction.skippedCount,
      release: () => {
        if (released) return;
        released = true;
        createdUrls.forEach((url) => URL.revokeObjectURL(url));
      },
    };
  } catch (error) {
    createdUrls.forEach((url) => URL.revokeObjectURL(url));
    if (signal.aborted) throw abortError();
    throw error;
  } finally {
    signal.removeEventListener("abort", onAbort);
    ffmpeg.off("log", logListener);
    if (mounted && !signal.aborted && ffmpeg.loaded) {
      await ffmpeg.unmount("/source").catch(() => undefined);
    }
    ffmpeg.terminate();
  }
};
