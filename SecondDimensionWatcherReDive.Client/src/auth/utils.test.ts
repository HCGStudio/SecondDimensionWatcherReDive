import { revokeSession } from "./utils";

type TestCallback = () => void | Promise<void>;
type TestFunction = (name: string, callback: TestCallback) => void;

declare const require: (specifier: string) => unknown;

const { rejects, strictEqual } = require("node:assert") as {
  rejects: (callback: () => Promise<unknown>) => Promise<void>;
  strictEqual: (actual: unknown, expected: unknown) => void;
};
const { describe, it } = require("node:test") as {
  describe: TestFunction;
  it: TestFunction;
};

describe("revokeSession", () => {
  it("rejects every non-success response, including unauthorized", async () => {
    const originalFetch = globalThis.fetch;
    globalThis.fetch = async () => new Response(null, { status: 401 });
    try {
      await rejects(() =>
        revokeSession({
          token: "access",
          refreshToken: "refresh",
          success: true,
        }),
      );
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it("sends the refresh credential only in the request body", async () => {
    const originalFetch = globalThis.fetch;
    let requestedUrl = "";
    let requestedBody = "";
    globalThis.fetch = async (input, init) => {
      requestedUrl = String(input);
      requestedBody = String(init?.body);
      return new Response(null, { status: 204 });
    };
    try {
      await revokeSession({
        token: "access",
        refreshToken: "refresh-secret",
        success: true,
      });
    } finally {
      globalThis.fetch = originalFetch;
    }

    strictEqual(requestedUrl, "/api/auth/logout");
    strictEqual(requestedUrl.includes("refresh-secret"), false);
    strictEqual(JSON.parse(requestedBody).refreshToken, "refresh-secret");
  });
});
