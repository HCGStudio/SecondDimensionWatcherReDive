import assert from "node:assert/strict";
import { afterEach, beforeEach, test } from "node:test";

import { clearAuth, setAuthResult } from "../auth/httpClient";
import { savePlaybackProgress } from "./api";

const originalFetch = globalThis.fetch;
const originalLocalStorage = Object.getOwnPropertyDescriptor(
  globalThis,
  "localStorage",
);

beforeEach(() => {
  const values = new Map<string, string>();
  Object.defineProperty(globalThis, "localStorage", {
    configurable: true,
    value: {
      getItem: (key: string) => values.get(key) ?? null,
      setItem: (key: string, value: string) => values.set(key, value),
      removeItem: (key: string) => values.delete(key),
      clear: () => values.clear(),
      key: (index: number) => [...values.keys()][index] ?? null,
      get length() {
        return values.size;
      },
    } satisfies Storage,
  });
  clearAuth();
});

afterEach(() => {
  globalThis.fetch = originalFetch;
  clearAuth();
  if (originalLocalStorage)
    Object.defineProperty(globalThis, "localStorage", originalLocalStorage);
  else delete (globalThis as { localStorage?: Storage }).localStorage;
});

test("playback progress sends the persisted position with keepalive and auth", async () => {
  let capturedInput: string | undefined;
  let capturedInit: RequestInit | undefined;
  globalThis.fetch = async (input, init) => {
    capturedInput = String(input);
    capturedInit = init;
    return Response.json({
      animationInfoId: "animation-1",
      path: "Season 1/EP01.mp4",
      virtualPath: "/anime/Season 1/EP01.mp4",
      positionSeconds: 125,
      durationSeconds: 1500,
      isWatched: false,
      updatedAt: "2026-08-29T00:00:00Z",
      watchedAt: null,
    });
  };
  setAuthResult({ success: true, token: "token", refreshToken: "refresh" });

  await savePlaybackProgress(
    {
      animationInfoId: "animation-1",
      path: "Season 1/EP01.mp4",
      positionSeconds: 125,
      durationSeconds: 1500,
    },
    true,
  );

  assert.equal(capturedInput, "/api/playback/progress");
  assert.equal(capturedInit?.method, "PUT");
  assert.equal(capturedInit?.keepalive, true);
  assert.equal(
    new Headers(capturedInit?.headers).get("Authorization"),
    "Bearer token",
  );
  assert.deepEqual(JSON.parse(String(capturedInit?.body)), {
    animationInfoId: "animation-1",
    path: "Season 1/EP01.mp4",
    positionSeconds: 125,
    durationSeconds: 1500,
  });
});
