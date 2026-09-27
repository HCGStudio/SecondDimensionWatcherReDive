import subtitleParserScriptUrl from "raw-url:matroska-subtitles/dist/matroska-subtitles.min.js";

import type {
  ExtractMkvSubtitlesOptions,
  ExtractedMkvSubtitles,
} from "./subtitleText";

export * from "./subtitleText";

const abortError = (signal?: AbortSignal): Error => {
  if (signal?.reason instanceof Error) return signal.reason;
  return new DOMException("The operation was aborted.", "AbortError");
};

const throwIfAborted = (signal?: AbortSignal): void => {
  if (signal?.aborted) throw abortError(signal);
};

/**
 * Downloads and parses MKV subtitles in a dedicated worker. Only the completed
 * WebVTT blobs and throttled progress updates cross back to the player thread.
 * The caller owns the returned URLs and must clean up when changing media.
 */
export const extractMkvSubtitles = async (
  input: RequestInfo | URL,
  options: ExtractMkvSubtitlesOptions = {},
): Promise<ExtractedMkvSubtitles> => {
  throwIfAborted(options.signal);
  const { createSubtitleWorker } = await import("./subtitleWorkerClient");
  throwIfAborted(options.signal);

  const request = new Request(input, options.requestInit);
  const body = request.body ? await request.arrayBuffer() : undefined;
  throwIfAborted(options.signal);
  const worker = createSubtitleWorker();

  return await new Promise<ExtractedMkvSubtitles>((resolve, reject) => {
    const dispose = () => {
      options.signal?.removeEventListener("abort", onAbort);
      worker.onmessage = null;
      worker.onerror = null;
      worker.onmessageerror = null;
      worker.terminate();
    };
    const fail = (error: unknown) => {
      dispose();
      reject(error);
    };
    const onAbort = () => fail(abortError(options.signal));
    options.signal?.addEventListener("abort", onAbort, { once: true });
    worker.onerror = (event) => {
      event.preventDefault();
      fail(
        new Error(event.message || "Failed to load the MKV subtitle worker."),
      );
    };
    worker.onmessageerror = () => {
      fail(new Error("Failed to receive the MKV subtitle worker result."));
    };
    worker.onmessage = (
      event: MessageEvent<
        import("./subtitleWorkerClient").SubtitleWorkerResponse
      >,
    ) => {
      const message = event.data;
      if (message.type === "progress") {
        try {
          options.onProgress?.(message.progress);
        } catch (error) {
          fail(error);
        }
        return;
      }
      if (message.type === "error") {
        fail(new Error(message.message));
        return;
      }

      const urls: string[] = [];
      try {
        const tracks = message.tracks.map(({ blob, ...track }) => {
          const url = URL.createObjectURL(blob);
          urls.push(url);
          return { ...track, url };
        });
        dispose();
        let cleanedUp = false;
        resolve({
          tracks,
          skippedTrackCount: message.skippedTrackCount,
          cleanup: () => {
            if (cleanedUp) return;
            cleanedUp = true;
            for (const url of urls) URL.revokeObjectURL(url);
          },
        });
      } catch (error) {
        for (const url of urls) URL.revokeObjectURL(url);
        fail(error);
      }
    };

    try {
      worker.postMessage({
        url: request.url,
        requestInit: {
          method: request.method,
          headers: Array.from(request.headers.entries()),
          credentials: request.credentials,
          cache: request.cache,
          mode: request.mode,
          redirect: request.redirect,
          referrer: request.referrer,
          referrerPolicy: request.referrerPolicy,
          integrity: request.integrity,
          keepalive: request.keepalive,
          priority: options.requestInit?.priority,
          body,
        },
        parserScriptUrl: new URL(
          options.parserScriptUrl ?? subtitleParserScriptUrl,
          document.baseURI,
        ).href,
        defaultCueDurationMs: options.defaultCueDurationMs,
      } satisfies import("./subtitleWorkerClient").SubtitleWorkerRequest);
    } catch (error) {
      fail(error);
    }
  });
};
