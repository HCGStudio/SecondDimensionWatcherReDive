export const MKV_TEXT_SUBTITLE_FORMATS = ["utf8", "ass", "ssa"] as const;

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

const DEFAULT_CUE_DURATION_MS = 2_000;
const supportedFormats = new Set<string>(MKV_TEXT_SUBTITLE_FORMATS);

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
