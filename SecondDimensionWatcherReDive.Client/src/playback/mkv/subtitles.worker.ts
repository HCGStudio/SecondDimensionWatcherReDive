type MkvSubtitleCue = import("./subtitleText").MkvSubtitleCue;
type MkvTextSubtitleFormat = import("./subtitleText").MkvTextSubtitleFormat;
type SubtitleWorkerRequest =
  import("./subtitleWorkerClient").SubtitleWorkerRequest;
type SubtitleWorkerResponse =
  import("./subtitleWorkerClient").SubtitleWorkerResponse;

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

interface EbmlElement {
  id: number;
  data?: unknown;
  Children?: EbmlElement[];
}

interface ParserEvents {
  on(event: string, listener: (...args: unknown[]) => void): this;
  once(event: string, listener: (...args: unknown[]) => void): this;
}

interface SubtitleParserInstance extends ParserEvents {
  destroyed?: boolean;
  writableEnded?: boolean;
  decoder: ParserEvents;
  write(chunk: Uint8Array, callback: (error?: Error | null) => void): boolean;
  end(): void;
  destroy(): void;
}

interface CollectedTrack {
  metadata: ParserTrack;
  format: MkvTextSubtitleFormat;
  cues: MkvSubtitleCue[];
}

const workerScope = globalThis as unknown as {
  importScripts: (...urls: string[]) => void;
  onmessage: ((event: MessageEvent<SubtitleWorkerRequest>) => void) | null;
  postMessage: (message: SubtitleWorkerResponse) => void;
  MatroskaSubtitles?: {
    SubtitleParser: new () => SubtitleParserInstance;
  };
};

const TRACKS_ID = 0x1654ae6b;
const TRACK_ENTRY_ID = 0xae;
const TRACK_NUMBER_ID = 0xd7;
const TRACK_TYPE_ID = 0x83;
const LANGUAGE_IETF_ID = 0x22b59d;
const PROGRESS_INTERVAL_MS = 250;

const childData = (element: EbmlElement, id: number): unknown =>
  element.Children?.find((child) => child.id === id)?.data;

const contentLength = (response: Response): number | null => {
  const header = response.headers.get("content-length");
  if (!header?.trim()) return null;
  const parsed = Number(header);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
};

const extract = async (
  options: SubtitleWorkerRequest,
): Promise<SubtitleWorkerResponse> => {
  // Classic workers support importScripts and dynamic imports, but cannot
  // contain static ES module imports. Keep text helpers in their own module.
  const { buildWebVtt, cleanSubtitleText, normalizeMkvSubtitleFormat } =
    await import("./subtitleText");
  // The Yarn patch makes the bundle's process.nextTick use globalThis so it
  // also works in this worker, where window is unavailable.
  // Parcel must preserve the classic script and must not tree-shake its
  // MatroskaSubtitles global assignment or turn it into a module-local value.
  workerScope.importScripts(options.parserScriptUrl);
  const library = workerScope.MatroskaSubtitles;
  if (!library?.SubtitleParser) {
    throw new Error("The MKV subtitle parser did not expose SubtitleParser.");
  }

  const parser = new library.SubtitleParser();
  const controller = new AbortController();
  const collected = new Map<number, CollectedTrack>();
  const skippedTrackNumbers = new Set<number>();
  let parserError: unknown = null;
  let finishParser: () => void = () => {};
  const parserFinished = new Promise<void>((resolve) => {
    finishParser = resolve;
  });
  const onParserError = (...args: unknown[]) => {
    parserError = args[0] ?? new Error("Failed to parse MKV subtitles.");
    controller.abort(parserError);
    finishParser();
  };

  parser.on("tracks", (...args: unknown[]) => {
    for (const metadata of (args[0] as ParserTrack[]) ?? []) {
      if (collected.has(metadata.number)) continue;
      const format = normalizeMkvSubtitleFormat(metadata.type);
      if (format) {
        collected.set(metadata.number, { metadata, format, cues: [] });
      } else {
        skippedTrackNumbers.add(metadata.number);
      }
    }
  });
  parser.on("subtitle", (...args: unknown[]) => {
    const subtitle = args[0] as ParserSubtitle;
    const track = collected.get(Number(args[1]));
    if (!track || !subtitle || typeof subtitle.text !== "string") return;
    track.cues.push({
      text: subtitle.text,
      startMs: subtitle.time,
      durationMs: subtitle.duration,
    });
  });
  parser.once("error", onParserError);
  parser.once("finish", finishParser);
  parser.decoder.once("error", onParserError);
  parser.decoder.on("data", (...args: unknown[]) => {
    const element = args[0] as EbmlElement;
    if (element.id !== TRACKS_ID) return;

    // The library reports only S_TEXT tracks and ignores the newer IETF
    // language tag. Inspect its already-decoded Tracks element for both.
    for (const entry of element.Children ?? []) {
      if (
        entry.id !== TRACK_ENTRY_ID ||
        childData(entry, TRACK_TYPE_ID) !== 17
      ) {
        continue;
      }
      const trackNumber = Number(childData(entry, TRACK_NUMBER_ID));
      const track = collected.get(trackNumber);
      if (!track) {
        skippedTrackNumbers.add(trackNumber);
        continue;
      }
      const ietfLanguage = childData(entry, LANGUAGE_IETF_ID);
      const language =
        typeof ietfLanguage === "string"
          ? ietfLanguage
          : ietfLanguage instanceof Uint8Array
            ? new TextDecoder().decode(ietfLanguage)
            : null;
      if (language?.trim()) track.metadata.language = language.trim();
    }

    // Unsupported text codecs (as well as image subtitles) do not justify
    // downloading the rest of a potentially multi-gigabyte video.
    if (collected.size === 0 && !parser.writableEnded) parser.end();
  });

  let loadedBytes = 0;
  let totalBytes: number | null = null;
  let lastProgressAt = Number.NEGATIVE_INFINITY;
  const reportProgress = (force = false) => {
    const now = performance.now();
    if (!force && now - lastProgressAt < PROGRESS_INTERVAL_MS) return;
    lastProgressAt = now;
    workerScope.postMessage({
      type: "progress",
      progress: {
        loadedBytes,
        totalBytes,
        fraction: totalBytes ? Math.min(1, loadedBytes / totalBytes) : null,
      },
    });
  };
  const write = async (chunk: Uint8Array) => {
    await new Promise<void>((resolve, reject) => {
      parser.write(chunk, (error) => (error ? reject(error) : resolve()));
    });
    if (parserError) throw parserError;
    loadedBytes += chunk.byteLength;
    reportProgress();
  };

  try {
    const response = await fetch(options.url, {
      ...options.requestInit,
      signal: controller.signal,
      priority: options.requestInit.priority ?? "low",
    });
    if (!response.ok) {
      await response.body?.cancel();
      throw new Error(`Failed to fetch MKV subtitles (${response.status}).`);
    }
    totalBytes = contentLength(response);
    reportProgress(true);

    if (response.body) {
      const reader = response.body.getReader();
      try {
        while (!parser.writableEnded) {
          const { done, value } = await reader.read();
          if (done) break;
          await write(value);
        }
      } finally {
        await reader.cancel().catch(() => {});
        reader.releaseLock();
      }
    } else {
      await write(new Uint8Array(await response.arrayBuffer()));
    }
    if (!parser.writableEnded) parser.end();
    await parserFinished;
    if (parserError) throw parserError;
    reportProgress(true);

    const tracks = Array.from(collected.values()).map((track) => ({
      trackNumber: track.metadata.number,
      language: track.metadata.language?.trim() || null,
      label:
        track.metadata.name?.trim() ||
        track.metadata.language?.trim() ||
        `Subtitle ${track.metadata.number}`,
      sourceFormat: track.format,
      format: "vtt" as const,
      cueCount: track.cues.filter(
        (cue) =>
          Number.isFinite(cue.startMs) &&
          cleanSubtitleText(cue.text, track.format).length > 0,
      ).length,
      blob: new Blob(
        [buildWebVtt(track.cues, track.format, options.defaultCueDurationMs)],
        { type: "text/vtt;charset=utf-8" },
      ),
    }));
    return {
      type: "result",
      tracks,
      skippedTrackCount: skippedTrackNumbers.size,
    };
  } finally {
    controller.abort();
    if (!parser.destroyed) parser.destroy();
  }
};

workerScope.onmessage = (event) => {
  extract(event.data).then(
    (result) => workerScope.postMessage(result),
    (error: unknown) => {
      workerScope.postMessage({
        type: "error",
        message: error instanceof Error ? error.message : String(error),
      });
    },
  );
};
