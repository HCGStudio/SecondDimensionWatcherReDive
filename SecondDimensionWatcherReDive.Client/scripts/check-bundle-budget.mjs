import { readFile, readdir, stat, writeFile } from "node:fs/promises";
import path from "node:path";

const distDirectory = path.resolve(process.argv[2] ?? "dist");
const budgets = {
  initialJavaScriptBytes: 800_000,
  homeRouteJavaScriptBytes: 800_000,
  asyncJavaScriptBytes: 650_000,
  ffmpegWasmBytes: 34_000_000,
};

const html = await readFile(path.join(distDirectory, "index.html"), "utf8");
const scriptTags = html.match(/<script\b[^>]*>/gi) ?? [];
const moduleScript = scriptTags.find((tag) =>
  /\btype=(?:["']module["']|module)(?:\s|>)/i.test(tag),
);
const sourceMatch = moduleScript?.match(
  /\bsrc=(?:["']([^"']+)["']|([^\s>]+))/i,
);
const entryName = (sourceMatch?.[1] ?? sourceMatch?.[2] ?? "").replace(
  /^\//,
  "",
);
if (!entryName) throw new Error("Unable to find the production module entry");

const fileNames = await readdir(distDirectory);
const assets = await Promise.all(
  fileNames.map(async (name) => ({
    name,
    bytes: (await stat(path.join(distDirectory, name))).size,
  })),
);
const entry = assets.find((asset) => asset.name === entryName);
if (!entry) throw new Error(`Missing entry asset: ${entryName}`);

const asyncJavaScript = assets
  .filter((asset) => asset.name.endsWith(".js") && asset.name !== entryName)
  .sort((left, right) => right.bytes - left.bytes);
const mainPageJavaScript = assets.filter(
  (asset) => asset.name.startsWith("MainPage.") && asset.name.endsWith(".js"),
);
const homeRouteJavaScriptBytes =
  entry.bytes +
  mainPageJavaScript.reduce((total, asset) => total + asset.bytes, 0);
const wasmAssets = assets
  .filter((asset) => asset.name.endsWith(".wasm"))
  .sort((left, right) => right.bytes - left.bytes);
const requiredRouteChunks = [
  "MainPage.",
  "PlayerPage.",
  "ChatPage.",
  "MetadataReviewPage.",
];
const entrySource = await readFile(path.join(distDirectory, entryName), "utf8");

const checks = [
  {
    name: "initial JavaScript",
    actual: entry.bytes,
    budget: budgets.initialJavaScriptBytes,
    passed: entry.bytes <= budgets.initialJavaScriptBytes,
  },
  {
    name: "home route JavaScript (entry + MainPage chunks)",
    actual: homeRouteJavaScriptBytes,
    budget: budgets.homeRouteJavaScriptBytes,
    passed: homeRouteJavaScriptBytes <= budgets.homeRouteJavaScriptBytes,
  },
  {
    name: "largest asynchronous JavaScript chunk",
    actual: asyncJavaScript[0]?.bytes ?? 0,
    budget: budgets.asyncJavaScriptBytes,
    passed: (asyncJavaScript[0]?.bytes ?? 0) <= budgets.asyncJavaScriptBytes,
  },
  {
    name: "FFmpeg WASM asset",
    actual: wasmAssets[0]?.bytes ?? 0,
    budget: budgets.ffmpegWasmBytes,
    passed:
      wasmAssets.length > 0 &&
      (wasmAssets[0]?.bytes ?? 0) <= budgets.ffmpegWasmBytes,
  },
  {
    name: "FFmpeg excluded from initial JavaScript",
    actual: /ffmpeg-core|transcodeMkvForBrowser|new FFmpeg/i.test(entrySource)
      ? 1
      : 0,
    budget: 0,
    passed: !/ffmpeg-core|transcodeMkvForBrowser|new FFmpeg/i.test(entrySource),
  },
  ...requiredRouteChunks.map((prefix) => ({
    name: `${prefix.slice(0, -1)} asynchronous chunk`,
    actual: assets.some((asset) => asset.name.startsWith(prefix)) ? 1 : 0,
    budget: 1,
    passed: assets.some((asset) => asset.name.startsWith(prefix)),
  })),
];

const report = {
  generatedAt: new Date().toISOString(),
  budgets,
  entry,
  mainPageJavaScript,
  homeRouteJavaScriptBytes,
  asyncJavaScript,
  wasmAssets,
  checks,
};
const rows = checks.map(
  (check) =>
    `| ${check.passed ? "✅" : "❌"} ${check.name} | ${check.actual.toLocaleString("en-US")} | ${check.budget.toLocaleString("en-US")} |`,
);
const markdown = [
  "# Frontend bundle budget",
  "",
  "| Check | Actual | Budget / expected |",
  "| --- | ---: | ---: |",
  ...rows,
  "",
  `Initial module: \`${entry.name}\``,
  "",
].join("\n");

await Promise.all([
  writeFile(
    path.join(distDirectory, "bundle-report.json"),
    `${JSON.stringify(report, null, 2)}\n`,
  ),
  writeFile(path.join(distDirectory, "bundle-report.md"), markdown),
]);
process.stdout.write(markdown);

if (checks.some((check) => !check.passed)) process.exitCode = 1;
