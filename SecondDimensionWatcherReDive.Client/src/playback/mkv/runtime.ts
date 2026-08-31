import type { MkvPlaybackProbe } from "./support";

export const isAbortError = (error: unknown): boolean =>
  error instanceof DOMException && error.name === "AbortError";

export const isMkvPath = (path: string): boolean =>
  /\.mkv(?:$|[?#])/i.test(path);

export type MkvPlaybackPlan = "mkvProxy" | "transcode";

/**
 * Direct Matroska playback through the MediaBunny proxy is preferred whenever
 * every selected track is browser-decodable. FFmpeg is the final fallback.
 */
export const chooseMkvPlaybackPlan = (
  probe: MkvPlaybackProbe | null,
): MkvPlaybackPlan =>
  probe?.videoDecodable && probe.audioDecodable ? "mkvProxy" : "transcode";
