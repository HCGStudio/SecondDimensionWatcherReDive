import { ApiError } from "../errors/apiError";
import fetcher, { clearAuth } from "./httpClient";

type TestCallback = () => void | Promise<void>;
type TestFunction = (name: string, callback: TestCallback) => void;

declare const require: (specifier: string) => unknown;

const { equal, rejects } = require("node:assert/strict") as {
  equal: (actual: unknown, expected: unknown) => void;
  rejects: (
    callback: () => Promise<unknown>,
    error: (value: unknown) => boolean,
  ) => Promise<void>;
};
const { afterEach, describe, it } = require("node:test") as {
  afterEach: (callback: TestCallback) => void;
  describe: TestFunction;
  it: TestFunction;
};

class TestStorage implements Storage {
  private readonly values = new Map<string, string>();

  get length() {
    return this.values.size;
  }

  clear() {
    this.values.clear();
  }

  getItem(key: string) {
    return this.values.get(key) ?? null;
  }

  key(index: number) {
    return [...this.values.keys()][index] ?? null;
  }

  removeItem(key: string) {
    this.values.delete(key);
  }

  setItem(key: string, value: string) {
    this.values.set(key, value);
  }
}

const storage = new TestStorage();
const originalFetch = globalThis.fetch;
Object.defineProperty(globalThis, "localStorage", {
  configurable: true,
  value: storage,
});

function storeAuth() {
  storage.setItem(
    "auth",
    JSON.stringify({
      token: "stored-token",
      refreshToken: "stored-refresh-token",
      success: true,
    }),
  );
}

describe("fetcher stored authentication", () => {
  afterEach(() => {
    clearAuth();
    storage.clear();
    globalThis.fetch = originalFetch;
  });

  it("propagates an API error without retrying anonymously", async () => {
    storeAuth();
    let requestCount = 0;
    globalThis.fetch = async (_input, init) => {
      requestCount += 1;
      equal(
        new Headers(init?.headers).get("Authorization"),
        "Bearer stored-token",
      );
      return new Response(JSON.stringify({ code: "forbidden" }), {
        status: 403,
        headers: { "Content-Type": "application/json" },
      });
    };

    await rejects(
      () => fetcher("/api/protected"),
      (error) =>
        error instanceof ApiError &&
        error.status === 403 &&
        error.code === "forbidden",
    );
    equal(requestCount, 1);
  });

  it("propagates a network failure without retrying anonymously", async () => {
    storeAuth();
    const networkFailure = new TypeError("network unavailable");
    let requestCount = 0;
    globalThis.fetch = async () => {
      requestCount += 1;
      throw networkFailure;
    };

    await rejects(
      () => fetcher("/api/protected"),
      (error) => error === networkFailure,
    );
    equal(requestCount, 1);
  });
});
