import { tmdbImageUrl } from "./tmdbImage";

type TestCallback = () => void | Promise<void>;
type TestFunction = (name: string, callback: TestCallback) => void;

declare const require: (specifier: string) => unknown;

const { equal } = require("node:assert/strict") as {
  equal: (actual: unknown, expected: unknown) => void;
};
const { describe, it } = require("node:test") as {
  describe: TestFunction;
  it: TestFunction;
};

describe("tmdbImageUrl", () => {
  it("routes approved poster paths through the local proxy", () => {
    equal(
      tmdbImageUrl("/abc-123.jpg", "w300"),
      "/api/images/tmdb/w300/abc-123.jpg",
    );
  });

  it("rejects missing, traversing, and unsupported poster paths", () => {
    equal(tmdbImageUrl(null), null);
    equal(tmdbImageUrl("/../poster.jpg"), null);
    equal(tmdbImageUrl("/poster.svg"), null);
  });
});
