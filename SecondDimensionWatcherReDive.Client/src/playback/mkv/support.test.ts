import { canCopyVideoCodecToMp4, isMkvPath } from "./support";

type TestCallback = () => void | Promise<void>;
type TestFunction = (name: string, callback: TestCallback) => void;

declare const require: (specifier: string) => unknown;

const { strictEqual } = require("node:assert") as {
  strictEqual: (actual: unknown, expected: unknown) => void;
};
const { describe, it } = require("node:test") as {
  describe: TestFunction;
  it: TestFunction;
};

describe("isMkvPath", () => {
  it("recognizes MKV paths case-insensitively", () => {
    strictEqual(isMkvPath("episode.mkv"), true);
    strictEqual(isMkvPath("Season 1/EP01.MKV"), true);
    strictEqual(isMkvPath("/anime/葬送のフリーレン.MkV"), true);
  });

  it("accepts query strings and fragments after the extension", () => {
    strictEqual(isMkvPath("episode.mkv?token=abc123"), true);
    strictEqual(isMkvPath("episode.mkv#track=2"), true);
    strictEqual(isMkvPath("episode.mkv?token=abc#track=2"), true);
  });

  it("rejects other extensions and extension-like substrings", () => {
    strictEqual(isMkvPath("episode.mp4"), false);
    strictEqual(isMkvPath("episode.mkv.backup"), false);
    strictEqual(isMkvPath("episode.mkvx"), false);
    strictEqual(isMkvPath("mkv"), false);
    strictEqual(isMkvPath("episode.mkv/segment"), false);
  });
});

describe("canCopyVideoCodecToMp4", () => {
  it("accepts AVC aliases and codec strings", () => {
    strictEqual(canCopyVideoCodecToMp4("avc"), true);
    strictEqual(canCopyVideoCodecToMp4("H.264"), true);
    strictEqual(canCopyVideoCodecToMp4("avc1.640028"), true);
  });

  it("rejects codecs whose WebCodecs support does not imply MP4 playback", () => {
    strictEqual(canCopyVideoCodecToMp4("vp8"), false);
    strictEqual(canCopyVideoCodecToMp4("vp9"), false);
    strictEqual(canCopyVideoCodecToMp4("av1"), false);
    strictEqual(canCopyVideoCodecToMp4("hevc"), false);
    strictEqual(canCopyVideoCodecToMp4(null), false);
  });
});
