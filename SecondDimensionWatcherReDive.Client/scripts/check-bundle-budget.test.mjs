import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";

const scriptPath = fileURLToPath(
  new URL("./check-bundle-budget.mjs", import.meta.url),
);

const createFixture = async ({ entryBytes, mainPageBytes }) => {
  const directory = await mkdtemp(path.join(tmpdir(), "sdw-bundle-budget-"));
  const assets = [
    ["app.js", entryBytes],
    ["MainPage.fixture.js", mainPageBytes],
    ["PlayerPage.fixture.js", 1],
    ["ChatPage.fixture.js", 1],
    ["MetadataReviewPage.fixture.js", 1],
    ["ffmpeg-core.fixture.wasm", 1],
  ];

  await Promise.all([
    writeFile(
      path.join(directory, "index.html"),
      '<script type="module" src="/app.js"></script>',
    ),
    ...assets.map(([name, bytes]) =>
      writeFile(path.join(directory, name), Buffer.alloc(bytes, "x")),
    ),
  ]);

  return directory;
};

const runBudgetCheck = (directory) =>
  spawnSync(process.execPath, [scriptPath, directory], {
    encoding: "utf8",
  });

describe("bundle budget", () => {
  it("passes when the entry and immediately loaded MainPage chunks fit together", async () => {
    const directory = await createFixture({
      entryBytes: 700_000,
      mainPageBytes: 50_000,
    });

    try {
      const result = runBudgetCheck(directory);
      assert.equal(result.status, 0, result.stderr || result.stdout);

      const report = JSON.parse(
        await readFile(path.join(directory, "bundle-report.json"), "utf8"),
      );
      assert.equal(report.homeRouteJavaScriptBytes, 750_000);
      assert.equal(report.mainPageJavaScript.length, 1);
    } finally {
      await rm(directory, { recursive: true, force: true });
    }
  });

  it("fails when MainPage pushes the home route over 800 KB", async () => {
    const directory = await createFixture({
      entryBytes: 700_000,
      mainPageBytes: 100_001,
    });

    try {
      const result = runBudgetCheck(directory);
      assert.equal(result.status, 1, result.stderr || result.stdout);
      assert.match(
        result.stdout,
        /home route JavaScript \(entry \+ MainPage chunks\).*800,001.*800,000/,
      );
    } finally {
      await rm(directory, { recursive: true, force: true });
    }
  });
});
