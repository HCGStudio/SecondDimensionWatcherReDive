import { readFile } from "node:fs/promises";
import { posix as path } from "node:path";
import { fileURLToPath } from "node:url";

const WINDOWS_ABSOLUTE_PATH = /^[A-Za-z]:\//;

function decodeXml(value) {
  return value.replace(
    /&(?:#(\d+)|#x([\dA-Fa-f]+)|amp|apos|gt|lt|quot);/g,
    (entity, decimal, hexadecimal) => {
      if (decimal !== undefined) {
        return String.fromCodePoint(Number.parseInt(decimal, 10));
      }
      if (hexadecimal !== undefined) {
        return String.fromCodePoint(Number.parseInt(hexadecimal, 16));
      }
      return {
        "&amp;": "&",
        "&apos;": "'",
        "&gt;": ">",
        "&lt;": "<",
        "&quot;": '"',
      }[entity];
    },
  );
}

function readAttribute(attributes, name) {
  const match = attributes.match(
    new RegExp(`(?:^|\\s)${name}\\s*=\\s*(?:"([^"]*)"|'([^']*)')`),
  );
  return match ? decodeXml(match[1] ?? match[2]) : undefined;
}

function normalizeCoveragePath(value) {
  let normalized = path.normalize(value.trim().replaceAll("\\", "/"));
  if (normalized !== "/" && !/^[A-Za-z]:\/$/.test(normalized)) {
    normalized = normalized.replace(/\/+$/, "");
  }
  return normalized === "." ? "" : normalized;
}

function isAbsoluteCoveragePath(value) {
  return value.startsWith("/") || WINDOWS_ABSOLUTE_PATH.test(value);
}

function pathStartsWith(fileName, source) {
  const caseInsensitive =
    WINDOWS_ABSOLUTE_PATH.test(fileName) || WINDOWS_ABSOLUTE_PATH.test(source);
  const comparableFileName = caseInsensitive
    ? fileName.toLowerCase()
    : fileName;
  const comparableSource = caseInsensitive ? source.toLowerCase() : source;
  return (
    comparableFileName === comparableSource ||
    comparableFileName.startsWith(`${comparableSource}/`)
  );
}

export function canonicalizeSourceFile(fileName, sources = []) {
  let canonical = normalizeCoveragePath(fileName);
  if (!canonical) throw new Error("Cobertura class filename is empty");

  if (isAbsoluteCoveragePath(canonical)) {
    const matchingSource = sources
      .map(normalizeCoveragePath)
      .filter(Boolean)
      .filter((source) => isAbsoluteCoveragePath(source))
      .filter((source) => pathStartsWith(canonical, source))
      .sort((left, right) => right.length - left.length)[0];
    if (matchingSource) {
      canonical = canonical.slice(matchingSource.length).replace(/^\/+/, "");
    }
  } else {
    canonical = canonical.replace(/^\.\/+/, "");
  }

  canonical = normalizeCoveragePath(canonical);
  if (!canonical)
    throw new Error(`Cobertura class filename is not a file: ${fileName}`);
  return canonical;
}

function readSources(xml) {
  return [...xml.matchAll(/<source\b[^>]*>([\s\S]*?)<\/source\s*>/gi)].map(
    (match) => decodeXml(match[1].trim()),
  );
}

export function collectInstrumentedLines(xml, reportName = "Cobertura report") {
  if (!/<coverage\b[^>]*>/i.test(xml)) {
    throw new Error(`Cobertura root is missing from ${reportName}`);
  }

  const sources = readSources(xml);
  const lines = new Map();
  const classPattern = /<class\b([^>]*)>([\s\S]*?)<\/class\s*>/gi;

  for (const classMatch of xml.matchAll(classPattern)) {
    const attributes = classMatch[1];
    const body = classMatch[2];
    const rawFileName = readAttribute(attributes, "filename");
    const lineElements = [...body.matchAll(/<line\b([^>]*)\/?\s*>/gi)];
    if (lineElements.length === 0) continue;
    if (rawFileName === undefined) {
      throw new Error(`Cobertura class filename is missing from ${reportName}`);
    }

    const fileName = canonicalizeSourceFile(rawFileName, sources);
    for (const lineElement of lineElements) {
      const numberText = readAttribute(lineElement[1], "number");
      const hitsText = readAttribute(lineElement[1], "hits");
      if (
        !/^[1-9]\d*$/.test(numberText ?? "") ||
        !/^\d+$/.test(hitsText ?? "")
      ) {
        throw new Error(
          `Invalid Cobertura line in ${reportName}: ${fileName}:${numberText ?? "?"}`,
        );
      }

      const key = `${fileName}\0${numberText}`;
      const covered = Number.parseInt(hitsText, 10) > 0;
      lines.set(key, (lines.get(key) ?? false) || covered);
    }
  }

  return lines;
}

export function mergeInstrumentedLines(target, source) {
  for (const [key, covered] of source) {
    target.set(key, (target.get(key) ?? false) || covered);
  }
  return target;
}

export async function aggregateCoverageReports(reports) {
  const lines = new Map();
  for (const report of reports) {
    const xml = await readFile(report, "utf8");
    mergeInstrumentedLines(lines, collectInstrumentedLines(xml, report));
  }
  return lines;
}

export function summarizeCoverage(lines) {
  const valid = lines.size;
  const covered = [...lines.values()].filter(Boolean).length;
  return { covered, valid, rate: valid === 0 ? 0 : covered / valid };
}

export async function main(args = process.argv.slice(2)) {
  const [minimumText, ...reports] = args;
  const minimum = Number.parseFloat(minimumText ?? "");
  if (
    !Number.isFinite(minimum) ||
    minimum < 0 ||
    minimum > 1 ||
    reports.length === 0
  ) {
    process.stderr.write(
      "usage: node check-dotnet-coverage.mjs MINIMUM_0_TO_1 coverage.cobertura.xml [...]\n",
    );
    return 2;
  }

  const { covered, valid, rate } = summarizeCoverage(
    await aggregateCoverageReports(reports),
  );
  const percent = (rate * 100).toFixed(2);
  const minimumPercent = (minimum * 100).toFixed(2);
  process.stdout.write(
    `Combined .NET line coverage: ${percent}% (${covered}/${valid}); minimum ${minimumPercent}%\n`,
  );
  return rate < minimum ? 1 : 0;
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  try {
    process.exitCode = await main();
  } catch (error) {
    process.stderr.write(`${error instanceof Error ? error.message : error}\n`);
    process.exitCode = 2;
  }
}
