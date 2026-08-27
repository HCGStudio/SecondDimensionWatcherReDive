import subtitleParserBundleSource from "bundle-text:matroska-subtitles/dist/matroska-subtitles.min.js";

export const MKV_TEXT_SUBTITLE_FORMATS = ["utf8", "ass", "ssa"] as const;

// The package's ESM entry imports Node's zlib. Its self-contained browser build
// declares a bundle-local variable, so expose that value explicitly before
// loading the code from an in-memory, same-origin asset.
const createBundledParserScriptUrl = (): string => {
  const exposedSource = subtitleParserBundleSource.replace(
    /([$_A-Za-z][\w$]*MatroskaSubtitles)\s*=\s*([$_A-Za-z][\w$]*)\s*;?\s*(\}\)\(\);?)/,
    "$1=globalThis.MatroskaSubtitles=$2;$3",
  );
  if (exposedSource === subtitleParserBundleSource) {
    throw new Error("Unable to expose the bundled MKV subtitle parser.");
  }
  return URL.createObjectURL(
    new Blob([exposedSource], { type: "text/javascript" }),
  );
};

export type MkvTextSubtitleFormat = (typeof MKV_TEXT_SUBTITLE_FORMATS)[number];

export interface MkvSubtitleCue {
  text: string;
  startMs: number;
  durationMs?: number | null;
}

export interface MkvSubtitleDownloadProgress {
  loadedBytes: number;
  totalBytes: number | null;
  /** A value between 0 and 1, or null when Content-Length is unavailable. */
  fraction: number | null;
}

export interface ExtractedMkvSubtitleTrack {
  trackNumber: number;
  language: string | null;
  label: string;
  sourceFormat: MkvTextSubtitleFormat;
  format: "vtt";
  cueCount: number;
  url: string;
}

export interface ExtractedMkvSubtitles {
  tracks: ExtractedMkvSubtitleTrack[];
  skippedTrackCount: number;
  /** Revokes every Blob URL in this result. Safe to call more than once. */
  cleanup: () => void;
}

export interface ExtractMkvSubtitlesOptions {
  signal?: AbortSignal;
  requestInit?: Omit<RequestInit, "signal">;
  onProgress?: (progress: MkvSubtitleDownloadProgress) => void;
  defaultCueDurationMs?: number;
  /** Override to serve the parser's browser bundle from the app origin. */
  parserScriptUrl?: string;
}

interface ParserTrack {
  number: number;
  language?: string;
  type: string;
  name?: string;
}

interface ParserSubtitle {
  text: string;
  time: number;
  duration?: number;
}

interface SubtitleParserInstance {
  destroyed?: boolean;
  writableEnded?: boolean;
  on(event: string, listener: (...args: unknown[]) => void): this;
  once(event: string, listener: (...args: unknown[]) => void): this;
  removeListener(event: string, listener: (...args: unknown[]) => void): this;
  write(chunk: Uint8Array): boolean;
  end(): void;
  destroy(error?: Error): void;
}

interface SubtitleParserLibrary {
  SubtitleParser: new () => SubtitleParserInstance;
}

interface CollectedTrack {
  metadata: ParserTrack;
  format: MkvTextSubtitleFormat;
  cues: MkvSubtitleCue[];
}

const DEFAULT_CUE_DURATION_MS = 2_000;
const PARSER_SCRIPT_ATTRIBUTE = "data-matroska-subtitles-parser";
const supportedFormats = new Set<string>(MKV_TEXT_SUBTITLE_FORMATS);

let parserLibraryPromise: Promise<SubtitleParserLibrary> | null = null;

const parserGlobal = (): SubtitleParserLibrary | undefined =>
  (
    globalThis as typeof globalThis & {
      MatroskaSubtitles?: SubtitleParserLibrary;
    }
  ).MatroskaSubtitles;

const abortError = (signal?: AbortSignal): Error => {
  if (signal?.reason instanceof Error) return signal.reason;
  return new DOMException("The operation was aborted.", "AbortError");
};

const throwIfAborted = (signal?: AbortSignal): void => {
  if (signal?.aborted) throw abortError(signal);
};

const awaitWithAbort = async <T>(
  promise: Promise<T>,
  signal?: AbortSignal,
): Promise<T> => {
  throwIfAborted(signal);
  if (!signal) return await promise;

  return await new Promise<T>((resolve, reject) => {
    const onAbort = () => reject(abortError(signal));
    signal.addEventListener("abort", onAbort, { once: true });
    promise.then(resolve, reject).finally(() => {
      signal.removeEventListener("abort", onAbort);
    });
  });
};

const loadSubtitleParserLibrary = async (
  signal?: AbortSignal,
  scriptUrl?: string,
): Promise<SubtitleParserLibrary> => {
  const existing = parserGlobal();
  if (existing?.SubtitleParser) return existing;

  if (!parserLibraryPromise) {
    parserLibraryPromise = new Promise<SubtitleParserLibrary>(
      (resolve, reject) => {
        if (typeof document === "undefined") {
          reject(
            new Error("MKV subtitle extraction requires a browser document."),
          );
          return;
        }

        let bundledScriptUrl: string | null = null;
        const script = document.createElement("script");
        script.async = true;
        try {
          bundledScriptUrl = scriptUrl ? null : createBundledParserScriptUrl();
          script.src = scriptUrl ?? bundledScriptUrl!;
        } catch (error) {
          reject(error);
          return;
        }
        script.setAttribute(PARSER_SCRIPT_ATTRIBUTE, "true");
        script.onload = () => {
          if (bundledScriptUrl) URL.revokeObjectURL(bundledScriptUrl);
          const loaded = parserGlobal();
          if (loaded?.SubtitleParser) {
            resolve(loaded);
          } else {
            reject(
              new Error(
                "The matroska-subtitles browser bundle did not expose SubtitleParser.",
              ),
            );
          }
        };
        script.onerror = () => {
          if (bundledScriptUrl) URL.revokeObjectURL(bundledScriptUrl);
          reject(new Error("Failed to load the MKV subtitle parser."));
        };
        document.head.appendChild(script);
      },
    ).catch((error: unknown) => {
      parserLibraryPromise = null;
      throw error;
    });
  }

  return await awaitWithAbort(parserLibraryPromise, signal);
};

export const normalizeMkvSubtitleFormat = (
  value: string | null | undefined,
): MkvTextSubtitleFormat | null => {
  const normalized = value?.trim().toLowerCase();
  return normalized && supportedFormats.has(normalized)
    ? (normalized as MkvTextSubtitleFormat)
    : null;
};

/** Converts milliseconds to the WebVTT HH:MM:SS.mmm timestamp form. */
export const formatWebVttTimestamp = (milliseconds: number): string => {
  const value = Number.isFinite(milliseconds)
    ? Math.max(0, Math.round(milliseconds))
    : 0;
  const hours = Math.floor(value / 3_600_000);
  const minutes = Math.floor((value % 3_600_000) / 60_000);
  const seconds = Math.floor((value % 60_000) / 1_000);
  const millis = value % 1_000;

  return `${hours.toString().padStart(2, "0")}:${minutes
    .toString()
    .padStart(2, "0")}:${seconds.toString().padStart(2, "0")}.${millis
    .toString()
    .padStart(3, "0")}`;
};

/** Removes ASS/SSA drawing/style commands while preserving readable cue text. */
export const cleanSubtitleText = (
  text: string,
  format: MkvTextSubtitleFormat,
): string => {
  let cleaned = text.replaceAll("\0", "").replace(/\r\n?/g, "\n");

  if (format === "ass" || format === "ssa") {
    cleaned = cleaned
      .replace(/\{[^{}]*\}/g, "")
      .replace(/\\[Nn]/g, "\n")
      .replace(/\\h/g, " ")
      .replace(/\\([{}])/g, "$1");
  }

  return cleaned
    .split("\n")
    .map((line) => line.trim())
    .join("\n")
    .trim();
};

const validDefaultDuration = (value?: number): number =>
  Number.isFinite(value) && value! > 0
    ? Math.round(value!)
    : DEFAULT_CUE_DURATION_MS;

const nextCueStart = (
  cues: readonly MkvSubtitleCue[],
  index: number,
  startMs: number,
): number | null => {
  for (let nextIndex = index + 1; nextIndex < cues.length; nextIndex += 1) {
    const candidate = cues[nextIndex].startMs;
    if (Number.isFinite(candidate) && candidate > startMs) return candidate;
  }
  return null;
};

/** Builds deterministic UTF-8 WebVTT text from parser cues. */
export const buildWebVtt = (
  inputCues: readonly MkvSubtitleCue[],
  format: MkvTextSubtitleFormat,
  defaultCueDurationMs = DEFAULT_CUE_DURATION_MS,
): string => {
  const fallbackDuration = validDefaultDuration(defaultCueDurationMs);
  const cues = inputCues
    .filter((cue) => Number.isFinite(cue.startMs))
    .map((cue, originalIndex) => ({ ...cue, originalIndex }))
    .sort(
      (left, right) =>
        left.startMs - right.startMs ||
        left.originalIndex - right.originalIndex,
    );

  const blocks: string[] = [];
  for (let index = 0; index < cues.length; index += 1) {
    const cue = cues[index];
    const text = cleanSubtitleText(cue.text, format);
    if (!text) continue;

    const startMs = Math.max(0, Math.round(cue.startMs));
    const explicitDuration = cue.durationMs;
    let endMs =
      Number.isFinite(explicitDuration) && explicitDuration! > 0
        ? startMs + Math.round(explicitDuration!)
        : (nextCueStart(cues, index, startMs) ?? startMs + fallbackDuration);
    endMs = Math.round(endMs);
    if (!Number.isFinite(endMs) || endMs <= startMs) {
      endMs = startMs + fallbackDuration;
    }

    blocks.push(
      `${blocks.length + 1}\n${formatWebVttTimestamp(startMs)} --> ${formatWebVttTimestamp(endMs)}\n${text}`,
    );
  }

  return blocks.length > 0
    ? `WEBVTT\n\n${blocks.join("\n\n")}\n`
    : "WEBVTT\n\n";
};

export const createWebVttBlobUrl = (webVtt: string): string =>
  URL.createObjectURL(new Blob([webVtt], { type: "text/vtt;charset=utf-8" }));

const contentLength = (response: Response): number | null => {
  const parsed = Number(response.headers.get("content-length"));
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
};

const reportProgress = (
  callback: ExtractMkvSubtitlesOptions["onProgress"],
  loadedBytes: number,
  totalBytes: number | null,
): void => {
  callback?.({
    loadedBytes,
    totalBytes,
    fraction:
      totalBytes && totalBytes > 0
        ? Math.min(1, loadedBytes / totalBytes)
        : null,
  });
};

const waitForDrain = async (
  parser: SubtitleParserInstance,
  signal?: AbortSignal,
): Promise<void> => {
  throwIfAborted(signal);

  await new Promise<void>((resolve, reject) => {
    const cleanup = () => {
      parser.removeListener("drain", onDrain);
      parser.removeListener("finish", onFinish);
      parser.removeListener("error", onError);
      signal?.removeEventListener("abort", onAbort);
    };
    const onDrain = () => {
      cleanup();
      resolve();
    };
    const onFinish = () => {
      cleanup();
      resolve();
    };
    const onError = (error: unknown) => {
      cleanup();
      reject(error);
    };
    const onAbort = () => {
      cleanup();
      reject(abortError(signal));
    };

    parser.once("drain", onDrain);
    parser.once("finish", onFinish);
    parser.once("error", onError);
    signal?.addEventListener("abort", onAbort, { once: true });
  });
};

const feedResponseToParser = async (
  response: Response,
  parser: SubtitleParserInstance,
  options: ExtractMkvSubtitlesOptions,
  getParserError: () => unknown,
): Promise<void> => {
  const totalBytes = contentLength(response);
  let loadedBytes = 0;
  reportProgress(options.onProgress, loadedBytes, totalBytes);

  if (!response.body) {
    const data = new Uint8Array(await response.arrayBuffer());
    throwIfAborted(options.signal);
    loadedBytes = data.byteLength;
    const canContinue = parser.write(data);
    const failure = getParserError();
    if (failure) throw failure;
    if (!canContinue && !parser.writableEnded) {
      await waitForDrain(parser, options.signal);
    }
    reportProgress(options.onProgress, loadedBytes, totalBytes);
    if (!parser.writableEnded) parser.end();
    return;
  }

  const reader = response.body.getReader();
  try {
    while (true) {
      throwIfAborted(options.signal);
      const { done, value } = await reader.read();
      if (done) break;

      loadedBytes += value.byteLength;
      const canContinue = parser.write(value);
      const failure = getParserError();
      if (failure) throw failure;
      if (!canContinue && !parser.writableEnded) {
        await waitForDrain(parser, options.signal);
      }
      reportProgress(options.onProgress, loadedBytes, totalBytes);

      // SubtitleParser ends itself after reading Tracks when the MKV has no
      // supported text subtitle tracks, so the rest of a large file is skipped.
      if (parser.writableEnded) {
        await reader.cancel();
        break;
      }
    }
  } finally {
    reader.releaseLock();
  }

  if (!parser.writableEnded) parser.end();
};

const trackLabel = (track: ParserTrack): string => {
  const name = track.name?.trim();
  if (name) return name;
  const language = track.language?.trim();
  return language || `Subtitle ${track.number}`;
};

/**
 * Streams an MKV through matroska-subtitles and exposes each supported text
 * track as a WebVTT Blob URL. The caller owns the returned URLs and must call
 * cleanup when changing media or unmounting the player.
 */
export const extractMkvSubtitles = async (
  input: RequestInfo | URL,
  options: ExtractMkvSubtitlesOptions = {},
): Promise<ExtractedMkvSubtitles> => {
  throwIfAborted(options.signal);

  const [{ SubtitleParser }, response] = await Promise.all([
    loadSubtitleParserLibrary(options.signal, options.parserScriptUrl),
    fetch(input, { ...options.requestInit, signal: options.signal }),
  ]);
  if (!response.ok) {
    throw new Error(`Failed to fetch MKV subtitles (${response.status}).`);
  }

  const parser = new SubtitleParser();
  const collected = new Map<number, CollectedTrack>();
  const seenTrackNumbers = new Set<number>();
  const trackOrder: number[] = [];
  let skippedTrackCount = 0;
  let parserError: unknown = null;
  let finishParser: (() => void) | null = null;
  const parserFinished = new Promise<void>((resolve) => {
    finishParser = resolve;
  });

  parser.on("tracks", (...args: unknown[]) => {
    const tracks = (args[0] as ParserTrack[]) ?? [];
    for (const metadata of tracks) {
      if (seenTrackNumbers.has(metadata.number)) continue;
      seenTrackNumbers.add(metadata.number);
      const format = normalizeMkvSubtitleFormat(metadata.type);
      if (!format) {
        skippedTrackCount += 1;
        continue;
      }
      collected.set(metadata.number, { metadata, format, cues: [] });
      trackOrder.push(metadata.number);
    }
  });
  parser.on("subtitle", (...args: unknown[]) => {
    const subtitle = args[0] as ParserSubtitle;
    const trackNumber = Number(args[1]);
    const track = collected.get(trackNumber);
    if (!track || !subtitle || typeof subtitle.text !== "string") return;
    track.cues.push({
      text: subtitle.text,
      startMs: subtitle.time,
      durationMs: subtitle.duration,
    });
  });
  parser.once("error", (...args: unknown[]) => {
    parserError = args[0] ?? new Error("Failed to parse MKV subtitles.");
    finishParser?.();
  });
  parser.once("finish", () => finishParser?.());

  const onAbort = () => parser.destroy(abortError(options.signal));
  options.signal?.addEventListener("abort", onAbort, { once: true });

  try {
    await feedResponseToParser(response, parser, options, () => parserError);
    await parserFinished;
    throwIfAborted(options.signal);
    if (parserError) throw parserError;

    const urls: string[] = [];
    try {
      const tracks = trackOrder.map((trackNumber) => {
        const track = collected.get(trackNumber)!;
        const webVtt = buildWebVtt(
          track.cues,
          track.format,
          options.defaultCueDurationMs,
        );
        const url = createWebVttBlobUrl(webVtt);
        urls.push(url);
        return {
          trackNumber,
          language: track.metadata.language?.trim() || null,
          label: trackLabel(track.metadata),
          sourceFormat: track.format,
          format: "vtt" as const,
          cueCount: track.cues.filter(
            (cue) =>
              Number.isFinite(cue.startMs) &&
              cleanSubtitleText(cue.text, track.format).length > 0,
          ).length,
          url,
        };
      });

      let cleanedUp = false;
      return {
        tracks,
        skippedTrackCount,
        cleanup: () => {
          if (cleanedUp) return;
          cleanedUp = true;
          for (const url of urls) URL.revokeObjectURL(url);
        },
      };
    } catch (error) {
      for (const url of urls) URL.revokeObjectURL(url);
      throw error;
    }
  } catch (error) {
    if (!parser.destroyed) parser.destroy();
    throw error;
  } finally {
    options.signal?.removeEventListener("abort", onAbort);
  }
};
