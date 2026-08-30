import fetcher from "../auth/httpClient";

export type ServerTranscodingState =
  "queued" | "probing" | "transcoding" | "ready" | "failed" | "canceled";

export type ServerTranscodingStrategy = "direct" | "remux" | "transcode";

export interface ServerTranscodingSubtitle {
  path: string;
  virtualPath: string;
  language: string | null;
  label: string;
  format: "vtt";
  url: string;
}

export interface ServerTranscodingSession {
  sessionId: string;
  state: ServerTranscodingState;
  strategy: ServerTranscodingStrategy | null;
  isPlayable: boolean;
  cacheHit: boolean;
  progress: number | null;
  speed: number | null;
  queuePosition: number | null;
  error: string | null;
  videoCodec: string | null;
  audioCodec: string | null;
  statusUrl: string;
  cancelUrl: string;
  playbackUrl: string | null;
  subtitles: ServerTranscodingSubtitle[];
  unsupportedSubtitleCount: number;
}

export interface PrepareServerTranscodingRequest {
  id: string;
  path: string;
  quality?: "auto" | "720p" | "1080p";
  audioLanguage?: string | null;
  audioTrackLabel?: string | null;
  subtitleLanguage?: string | null;
  subtitleTrackLabel?: string | null;
}

const abortError = (): DOMException =>
  new DOMException("The operation was aborted", "AbortError");

const pollDelay = async (
  milliseconds: number,
  signal: AbortSignal,
): Promise<void> => {
  if (signal.aborted) throw abortError();
  await new Promise<void>((resolve, reject) => {
    const onAbort = () => {
      globalThis.clearTimeout(timeout);
      reject(abortError());
    };
    const timeout = globalThis.setTimeout(() => {
      signal.removeEventListener("abort", onAbort);
      resolve();
    }, milliseconds);
    signal.addEventListener("abort", onAbort, { once: true });
  });
};

export const prepareServerTranscoding = async (
  request: PrepareServerTranscodingRequest,
  signal: AbortSignal,
): Promise<ServerTranscodingSession> =>
  await fetcher("/api/transcoding/prepare", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
    signal,
  });

export const watchServerTranscoding = async (
  initial: ServerTranscodingSession,
  signal: AbortSignal,
  onUpdate: (session: ServerTranscodingSession) => void,
): Promise<ServerTranscodingSession> => {
  let current = initial;
  let transientFailures = 0;
  while (true) {
    if (signal.aborted) throw abortError();
    onUpdate(current);
    if (current.state === "ready") return current;
    if (current.state === "failed" || current.state === "canceled") {
      throw new Error(current.error || `Server transcoding ${current.state}`);
    }

    await pollDelay(current.state === "queued" ? 1000 : 750, signal);
    try {
      const response = await fetch(current.statusUrl, { signal });
      if (!response.ok)
        throw new Error(`Transcoding status ${response.status}`);
      current = (await response.json()) as ServerTranscodingSession;
      transientFailures = 0;
    } catch (error) {
      if (signal.aborted) throw abortError();
      transientFailures += 1;
      if (transientFailures >= 3) throw error;
    }
  }
};

export const touchServerTranscoding = async (
  statusUrl: string,
  signal: AbortSignal,
): Promise<void> => {
  const response = await fetch(statusUrl, { signal });
  if (!response.ok) throw new Error(`Transcoding status ${response.status}`);
  await response.json();
};

export const releaseServerTranscoding = (cancelUrl: string): void => {
  void fetch(cancelUrl, { method: "DELETE", keepalive: true }).catch(
    () => undefined,
  );
};
