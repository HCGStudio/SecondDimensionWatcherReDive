import assert from "node:assert/strict";
import { afterEach, beforeEach, test } from "node:test";

import fetcher, { clearAuth, setAuthResult } from "./httpClient";

class MemoryStorage implements Storage {
  readonly #values = new Map<string, string>();

  get length(): number {
    return this.#values.size;
  }

  clear(): void {
    this.#values.clear();
  }

  getItem(key: string): string | null {
    return this.#values.get(key) ?? null;
  }

  key(index: number): string | null {
    return [...this.#values.keys()][index] ?? null;
  }

  removeItem(key: string): void {
    this.#values.delete(key);
  }

  setItem(key: string, value: string): void {
    this.#values.set(key, value);
  }
}

const originalFetch = globalThis.fetch;
const originalLocalStorage = Object.getOwnPropertyDescriptor(
  globalThis,
  "localStorage",
);

beforeEach(() => {
  Object.defineProperty(globalThis, "localStorage", {
    configurable: true,
    value: new MemoryStorage(),
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

test("concurrent 401 responses share one refresh and retry with the new token", async () => {
  let refreshCalls = 0;
  const requestTokens: string[] = [];

  globalThis.fetch = async (input, init) => {
    if (String(input) === "/api/auth/refresh") {
      refreshCalls++;
      await new Promise((resolve) => setTimeout(resolve, 5));
      return Response.json({
        success: true,
        token: "new-token",
        refreshToken: "new-refresh-token",
      });
    }

    const token = new Headers(init?.headers).get("Authorization") ?? "";
    requestTokens.push(token);
    if (token === "Bearer old-token")
      return new Response(null, { status: 401 });
    return Response.json({ ok: true, path: String(input) });
  };
  setAuthResult({
    success: true,
    token: "old-token",
    refreshToken: "old-refresh-token",
  });

  const [first, second] = await Promise.all([
    fetcher<{ ok: boolean }>("/api/first"),
    fetcher<{ ok: boolean }>("/api/second"),
  ]);

  assert.equal(refreshCalls, 1);
  assert.equal(first.ok, true);
  assert.equal(second.ok, true);
  assert.deepEqual(requestTokens, [
    "Bearer old-token",
    "Bearer old-token",
    "Bearer new-token",
    "Bearer new-token",
  ]);
});

test("a failed non-authenticated request exposes its HTTP status", async () => {
  globalThis.fetch = async () => new Response(null, { status: 503 });

  await assert.rejects(fetcher("/api/unavailable"), /503/);
});
