export {};

type TestCallback = () => void | Promise<void>;
type TestFunction = (name: string, callback: TestCallback) => void;

interface SubtitleHelpers {
  normalizeMkvSubtitleFormat: (
    value: string | null | undefined,
  ) => "utf8" | "ass" | "ssa" | null;
  formatWebVttTimestamp: (milliseconds: number) => string;
  cleanSubtitleText: (text: string, format: "utf8" | "ass" | "ssa") => string;
  buildWebVtt: (
    cues: readonly {
      text: string;
      startMs: number;
      durationMs?: number | null;
    }[],
    format: "utf8" | "ass" | "ssa",
    defaultCueDurationMs?: number,
  ) => string;
}

interface TypeScriptRuntime {
  ModuleKind: { CommonJS: number };
  ScriptTarget: { ES2022: number };
  transpileModule: (
    source: string,
    options: {
      compilerOptions: { module: number; target: number };
    },
  ) => { outputText: string };
}

interface TestRequire {
  (specifier: string): unknown;
  resolve: (specifier: string) => string;
}

declare const require: TestRequire;

const { strictEqual } = require("node:assert") as {
  strictEqual: (actual: unknown, expected: unknown) => void;
};
const { describe, it } = require("node:test") as {
  describe: TestFunction;
  it: TestFunction;
};
const { readFileSync } = require("node:fs") as {
  readFileSync: (path: string, encoding: "utf8") => string;
};
const typescript = require("typescript") as TypeScriptRuntime;

// Parcel-specific asset schemes are unavailable in a Node test process. Load
// the real source, replace only that inert top-level asset import, and let the
// project's TypeScript compiler transpile the module before exercising it.
const subtitleSourcePath = require.resolve("./subtitles.ts");
const subtitleSource = readFileSync(subtitleSourcePath, "utf8").replace(
  /^import\s+([$_A-Za-z][\w$]*)\s+from\s+["'](?:raw-url|bundle-text):[^"']+["'];/m,
  'const $1 = "test-parser.js";',
);
const compiledSubtitleSource = typescript.transpileModule(subtitleSource, {
  compilerOptions: {
    module: typescript.ModuleKind.CommonJS,
    target: typescript.ScriptTarget.ES2022,
  },
}).outputText;
const subtitleModule = { exports: {} as Record<string, unknown> };
const evaluateSubtitleModule = new Function(
  "exports",
  "module",
  "require",
  compiledSubtitleSource,
) as (
  exports: Record<string, unknown>,
  module: { exports: Record<string, unknown> },
  requireFunction: typeof require,
) => void;
evaluateSubtitleModule(subtitleModule.exports, subtitleModule, require);
const {
  buildWebVtt,
  cleanSubtitleText,
  formatWebVttTimestamp,
  normalizeMkvSubtitleFormat,
} = subtitleModule.exports as unknown as SubtitleHelpers;

describe("normalizeMkvSubtitleFormat", () => {
  it("normalizes supported formats and rejects unsupported ones", () => {
    strictEqual(normalizeMkvSubtitleFormat(" UTF8 "), "utf8");
    strictEqual(normalizeMkvSubtitleFormat("ASS"), "ass");
    strictEqual(normalizeMkvSubtitleFormat("ssa"), "ssa");
    strictEqual(normalizeMkvSubtitleFormat("srt"), null);
    strictEqual(normalizeMkvSubtitleFormat(undefined), null);
  });
});

describe("formatWebVttTimestamp", () => {
  it("formats hours, minutes, seconds, and milliseconds", () => {
    strictEqual(formatWebVttTimestamp(3_723_004), "01:02:03.004");
    strictEqual(formatWebVttTimestamp(360_000_009), "100:00:00.009");
  });

  it("rounds to the nearest millisecond across second boundaries", () => {
    strictEqual(formatWebVttTimestamp(999.4), "00:00:00.999");
    strictEqual(formatWebVttTimestamp(999.5), "00:00:01.000");
  });

  it("clamps negative and non-finite values to zero", () => {
    strictEqual(formatWebVttTimestamp(-1), "00:00:00.000");
    strictEqual(formatWebVttTimestamp(Number.NaN), "00:00:00.000");
    strictEqual(
      formatWebVttTimestamp(Number.POSITIVE_INFINITY),
      "00:00:00.000",
    );
  });
});

describe("cleanSubtitleText", () => {
  it("removes ASS commands and converts ASS whitespace escapes", () => {
    const input =
      "{\\an8}{\\b1} First line\\NSecond line\\nThird\\hline\0\r\n{\\i1} Fourth {\\i0}";

    strictEqual(
      cleanSubtitleText(input, "ass"),
      "First line\nSecond line\nThird line\nFourth",
    );
  });

  it("applies the same cleanup to SSA cues", () => {
    strictEqual(
      cleanSubtitleText("{\\i1} Italic\\N{\\i0}Plain", "ssa"),
      "Italic\nPlain",
    );
  });

  it("normalizes line endings and NUL bytes without interpreting UTF-8 text as ASS", () => {
    strictEqual(
      cleanSubtitleText("  {literal}\\Nvalue\0\r\n next  ", "utf8"),
      "{literal}\\Nvalue\nnext",
    );
  });
});

describe("buildWebVtt", () => {
  it("sorts cues, filters invalid starts, cleans ASS, and computes cue ends", () => {
    const result = buildWebVtt(
      [
        { text: " Later ", startMs: 4_000, durationMs: 500 },
        { text: "{\\i1}First\\Nline{\\i0}", startMs: 1_000 },
        { text: "Second", startMs: 2_500, durationMs: 1_000 },
        { text: "Ignored", startMs: Number.NaN, durationMs: 500 },
      ],
      "ass",
    );

    strictEqual(
      result,
      "WEBVTT\n\n" +
        "1\n00:00:01.000 --> 00:00:02.500\nFirst\nline\n\n" +
        "2\n00:00:02.500 --> 00:00:03.500\nSecond\n\n" +
        "3\n00:00:04.000 --> 00:00:04.500\nLater\n",
    );
  });

  it("uses a rounded custom fallback duration for the final cue", () => {
    strictEqual(
      buildWebVtt([{ text: "Final", startMs: 10.5 }], "utf8", 1_234.6),
      "WEBVTT\n\n1\n00:00:00.011 --> 00:00:01.246\nFinal\n",
    );
  });

  it("falls back to two seconds when the requested duration is invalid", () => {
    strictEqual(
      buildWebVtt([{ text: "Final", startMs: 5_000 }], "utf8", 0),
      "WEBVTT\n\n1\n00:00:05.000 --> 00:00:07.000\nFinal\n",
    );
  });

  it("returns an empty WebVTT document when no readable cues remain", () => {
    strictEqual(
      buildWebVtt(
        [
          { text: "Ignored", startMs: Number.POSITIVE_INFINITY },
          { text: "{\\i1}{\\i0}\0", startMs: 1_000 },
        ],
        "ass",
      ),
      "WEBVTT\n\n",
    );
  });
});
