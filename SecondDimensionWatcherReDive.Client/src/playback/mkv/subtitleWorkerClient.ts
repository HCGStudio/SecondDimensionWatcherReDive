import type {
  ExtractedMkvSubtitleTrack,
  MkvSubtitleDownloadProgress,
} from "./subtitleText";

export interface SubtitleWorkerRequest {
  url: string;
  requestInit: RequestInit;
  parserScriptUrl: string;
  defaultCueDurationMs?: number;
}

export type SubtitleWorkerResponse =
  | { type: "progress"; progress: MkvSubtitleDownloadProgress }
  | { type: "error"; message: string }
  | {
      type: "result";
      tracks: (Omit<ExtractedMkvSubtitleTrack, "url"> & { blob: Blob })[];
      skippedTrackCount: number;
    };

export const createSubtitleWorker = (): Worker =>
  new Worker(new URL("./subtitles.worker.ts", import.meta.url));
