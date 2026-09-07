import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

import {
  canonicalizeSourceFile,
  collectInstrumentedLines,
  mergeInstrumentedLines,
  summarizeCoverage,
} from "./check-dotnet-coverage.mjs";

const reportA = `<?xml version="1.0"?>
<coverage lines-covered="999" lines-valid="999">
  <sources><source>/home/runner/work/repo/</source></sources>
  <packages><package><classes>
    <class filename="/home/runner/work/repo/src/Foo&amp;Bar.cs">
      <methods><method><lines>
        <line number="10" hits="0"/>
        <line number="11" hits="0"/>
      </lines></method></methods>
      <lines>
        <line number="10" hits="1"/>
        <line number="11" hits="0"/>
      </lines>
    </class>
  </classes></package></packages>
</coverage>`;

const reportB = `<?xml version="1.0"?>
<coverage lines-covered="0" lines-valid="1">
  <sources><source>C:\\agent\\repo\\</source></sources>
  <packages><package><classes>
    <class filename=".\\src\\Foo&amp;Bar.cs">
      <lines>
        <line number="10" hits="0"/>
        <line number="11" hits="3"/>
      </lines>
    </class>
    <class filename="src/Other.cs">
      <lines><line number="10" hits="0"/></lines>
    </class>
  </classes></package></packages>
</coverage>`;

test("canonicalizes relative, absolute, and Windows-style source paths", () => {
  assert.equal(
    canonicalizeSourceFile("/checkout/repo/src/../src/Foo.cs", [
      "/checkout/repo/",
    ]),
    "src/Foo.cs",
  );
  assert.equal(
    canonicalizeSourceFile("C:\\Agent\\Repo\\src\\Foo.cs", [
      "c:\\agent\\repo\\",
    ]),
    "src/Foo.cs",
  );
  assert.equal(canonicalizeSourceFile(".\\src\\Foo.cs"), "src/Foo.cs");
});

test("merges overlapping reports by canonical file and line with covered winning", () => {
  const lines = collectInstrumentedLines(reportA, "report A");
  mergeInstrumentedLines(lines, collectInstrumentedLines(reportB, "report B"));
  mergeInstrumentedLines(
    lines,
    collectInstrumentedLines(reportA, "duplicate report A"),
  );

  assert.deepEqual(summarizeCoverage(lines), {
    covered: 2,
    valid: 3,
    rate: 2 / 3,
  });
});

test("rejects malformed line coverage instead of trusting root totals", () => {
  assert.throws(
    () =>
      collectInstrumentedLines(
        '<coverage lines-covered="1" lines-valid="1"><class filename="a.cs"><lines><line number="1"/></lines></class></coverage>',
        "malformed.xml",
      ),
    /Invalid Cobertura line in malformed\.xml/,
  );
});

test("CLI reports the merged total and enforces the threshold", async (context) => {
  const fixtureDirectory = await mkdtemp(join(tmpdir(), "sdw-coverage-"));
  context.after(() => rm(fixtureDirectory, { recursive: true, force: true }));
  const first = join(fixtureDirectory, "first.xml");
  const second = join(fixtureDirectory, "second.xml");
  await Promise.all([writeFile(first, reportA), writeFile(second, reportB)]);

  const script = fileURLToPath(
    new URL("./check-dotnet-coverage.mjs", import.meta.url),
  );
  const passing = spawnSync(
    process.execPath,
    [script, "0.66", first, second, first],
    {
      encoding: "utf8",
    },
  );
  assert.equal(passing.status, 0, passing.stderr);
  assert.equal(
    passing.stdout,
    "Combined .NET line coverage: 66.67% (2/3); minimum 66.00%\n",
  );

  const failing = spawnSync(process.execPath, [script, "0.67", first, second], {
    encoding: "utf8",
  });
  assert.equal(failing.status, 1, failing.stderr);
});
