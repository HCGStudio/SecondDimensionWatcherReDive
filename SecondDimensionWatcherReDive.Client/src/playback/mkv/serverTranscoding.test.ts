import {
  ServerTranscodingSession,
  watchServerTranscoding,
} from "../serverTranscoding";

type TestCallback = () => void | Promise<void>;
type TestFunction = (name: string, callback: TestCallback) => void;

declare const require: (specifier: string) => unknown;

const { rejects, strictEqual } = require("node:assert") as {
  rejects: (
    promise: Promise<unknown>,
    check: (error: unknown) => boolean,
  ) => Promise<void>;
  strictEqual: (actual: unknown, expected: unknown) => void;
};
const { describe, it } = require("node:test") as {
  describe: TestFunction;
  it: TestFunction;
};

const createSession = (
  state: ServerTranscodingSession["state"],
): ServerTranscodingSession => ({
  sessionId: "session",
  state,
  strategy: state === "queued" ? null : "remux",
  isPlayable: state === "ready",
  cacheHit: false,
  progress: null,
  speed: null,
  queuePosition: state === "queued" ? 1 : null,
  error: state === "failed" ? "fixture failure" : null,
  videoCodec: null,
  audioCodec: null,
  statusUrl: "/status",
  cancelUrl: "/cancel",
  playbackUrl: state === "ready" ? "/media.m3u8" : null,
  subtitles: [],
  unsupportedSubtitleCount: 0,
});

describe("watchServerTranscoding", () => {
  it("returns an already-ready cache entry without polling", async () => {
    const controller = new AbortController();
    let updates = 0;
    const result = await watchServerTranscoding(
      createSession("ready"),
      controller.signal,
      () => {
        updates += 1;
      },
    );

    strictEqual(result.state, "ready");
    strictEqual(updates, 1);
  });

  it("surfaces terminal server failures", async () => {
    await rejects(
      watchServerTranscoding(
        createSession("failed"),
        new AbortController().signal,
        () => undefined,
      ),
      (error) => error instanceof Error && error.message === "fixture failure",
    );
  });

  it("aborts queue polling when the player is closed", async () => {
    const controller = new AbortController();
    const pending = watchServerTranscoding(
      createSession("queued"),
      controller.signal,
      () => controller.abort(),
    );

    await rejects(
      pending,
      (error) => error instanceof DOMException && error.name === "AbortError",
    );
  });
});
