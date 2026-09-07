import { renderToStaticMarkup } from "react-dom/server";

import {
  PlaybackErrorActions,
  reloadPlaybackLocation,
} from "./PlaybackErrorActions";

type TestCallback = () => void | Promise<void>;
type TestFunction = (name: string, callback: TestCallback) => void;

declare const require: (specifier: string) => unknown;

const assert = require("node:assert/strict") as {
  doesNotMatch: (value: string, regexp: RegExp) => void;
  equal: (actual: unknown, expected: unknown) => void;
  match: (value: string, regexp: RegExp) => void;
};
const { describe, it } = require("node:test") as {
  describe: TestFunction;
  it: TestFunction;
};

describe("PlaybackErrorActions", () => {
  it("offers a reload retry for playback preparation failures", () => {
    const html = renderToStaticMarkup(
      <PlaybackErrorActions
        backLabel="Back"
        retryLabel="Reload and retry"
        showRetry
        onBack={() => undefined}
        onRetry={() => undefined}
      />,
    );

    assert.match(html, /Reload and retry/);
    assert.match(html, /Back/);
  });

  it("keeps validation and context errors as back-only failures", () => {
    const html = renderToStaticMarkup(
      <PlaybackErrorActions
        backLabel="Back"
        retryLabel="Reload and retry"
        showRetry={false}
        onBack={() => undefined}
      />,
    );

    assert.doesNotMatch(html, /Reload and retry/);
    assert.match(html, /Back/);
  });

  it("reloads the current page when retry is selected", () => {
    let reloadCount = 0;

    reloadPlaybackLocation({
      reload: () => {
        reloadCount += 1;
      },
    });

    assert.equal(reloadCount, 1);
  });
});
