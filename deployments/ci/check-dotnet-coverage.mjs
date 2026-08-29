import { readFile } from "node:fs/promises";

const [minimumText, ...reports] = process.argv.slice(2);
const minimum = Number.parseFloat(minimumText ?? "");
if (!Number.isFinite(minimum) || minimum < 0 || minimum > 1 || reports.length === 0) {
  process.stderr.write(
    "usage: node check-dotnet-coverage.mjs MINIMUM_0_TO_1 coverage.cobertura.xml [...]\n",
  );
  process.exit(2);
}

let covered = 0;
let valid = 0;
for (const report of reports) {
  const xml = await readFile(report, "utf8");
  const root = xml.match(/<coverage\b[^>]*>/)?.[0];
  const coveredMatch = root?.match(/\blines-covered="(\d+)"/);
  const validMatch = root?.match(/\blines-valid="(\d+)"/);
  if (!coveredMatch || !validMatch) {
    throw new Error(`Cobertura totals are missing from ${report}`);
  }
  covered += Number.parseInt(coveredMatch[1], 10);
  valid += Number.parseInt(validMatch[1], 10);
}

const rate = valid === 0 ? 0 : covered / valid;
const percent = (rate * 100).toFixed(2);
const minimumPercent = (minimum * 100).toFixed(2);
process.stdout.write(
  `Combined .NET line coverage: ${percent}% (${covered}/${valid}); minimum ${minimumPercent}%\n`,
);
if (rate < minimum) process.exit(1);
