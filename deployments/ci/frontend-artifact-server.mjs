import { createReadStream } from "node:fs";
import { stat } from "node:fs/promises";
import { createServer } from "node:http";
import { request as httpRequest } from "node:http";
import { request as httpsRequest } from "node:https";
import { extname, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

const PRODUCTION_HEADER = "X-SDW-Frontend-Artifact";
const HOP_BY_HOP_HEADERS = [
  "connection",
  "keep-alive",
  "proxy-authenticate",
  "proxy-authorization",
  "te",
  "trailer",
  "transfer-encoding",
  "upgrade",
];
const CONTENT_TYPES = new Map([
  [".css", "text/css; charset=utf-8"],
  [".gif", "image/gif"],
  [".html", "text/html; charset=utf-8"],
  [".ico", "image/x-icon"],
  [".jpeg", "image/jpeg"],
  [".jpg", "image/jpeg"],
  [".js", "text/javascript; charset=utf-8"],
  [".json", "application/json; charset=utf-8"],
  [".mjs", "text/javascript; charset=utf-8"],
  [".png", "image/png"],
  [".svg", "image/svg+xml; charset=utf-8"],
  [".ttf", "font/ttf"],
  [".wasm", "application/wasm"],
  [".webp", "image/webp"],
  [".woff", "font/woff"],
  [".woff2", "font/woff2"],
]);

function removeHopByHopHeaders(headers) {
  const filtered = { ...headers };
  for (const name of HOP_BY_HOP_HEADERS) delete filtered[name];
  return filtered;
}

function sendText(response, status, message, method = "GET") {
  const body = `${message}\n`;
  response.writeHead(status, {
    "Cache-Control": "no-store",
    "Content-Length": Buffer.byteLength(body),
    "Content-Type": "text/plain; charset=utf-8",
    [PRODUCTION_HEADER]: "production",
  });
  response.end(method === "HEAD" ? undefined : body);
}

function proxyApiRequest(request, response, apiOrigin) {
  const incoming = new URL(request.url ?? "/", "http://frontend.invalid");
  const target = new URL(`${incoming.pathname}${incoming.search}`, apiOrigin);
  const headers = removeHopByHopHeaders(request.headers);
  headers.host = target.host;
  const transport = target.protocol === "https:" ? httpsRequest : httpRequest;
  const upstream = transport(
    target,
    { method: request.method, headers },
    (upstreamResponse) => {
      const responseHeaders = removeHopByHopHeaders(upstreamResponse.headers);
      response.writeHead(upstreamResponse.statusCode ?? 502, responseHeaders);
      upstreamResponse.pipe(response);
    },
  );

  upstream.on("error", (error) => {
    if (response.headersSent) {
      response.destroy(error);
      return;
    }
    sendText(
      response,
      502,
      `Mock API proxy failed: ${error.message}`,
      request.method,
    );
  });
  request.on("aborted", () => upstream.destroy());
  request.pipe(upstream);
}

function resolveArtifactPath(rootDirectory, pathname) {
  let decoded;
  try {
    decoded = decodeURIComponent(pathname);
  } catch {
    return { error: 400 };
  }
  if (decoded.includes("\0")) return { error: 400 };

  const relativePath = decoded.replace(/^\/+/, "");
  const candidate = resolve(rootDirectory, relativePath || "index.html");
  if (
    candidate !== rootDirectory &&
    !candidate.startsWith(`${rootDirectory}${sep}`)
  ) {
    return { error: 403 };
  }
  return { candidate };
}

async function findStaticFile(rootDirectory, pathname, acceptsHtml) {
  const resolved = resolveArtifactPath(rootDirectory, pathname);
  if (resolved.error) return resolved;

  try {
    const fileInfo = await stat(resolved.candidate);
    if (fileInfo.isFile()) return { fileInfo, filePath: resolved.candidate };
  } catch (error) {
    if (error?.code !== "ENOENT" && error?.code !== "ENOTDIR") throw error;
  }

  if (acceptsHtml && extname(pathname) === "") {
    const indexPath = resolve(rootDirectory, "index.html");
    return {
      fileInfo: await stat(indexPath),
      filePath: indexPath,
      spaFallback: true,
    };
  }
  return { error: 404 };
}

async function serveStaticArtifact(request, response, rootDirectory) {
  const method = request.method ?? "GET";
  if (method !== "GET" && method !== "HEAD") {
    sendText(response, 405, "Method not allowed", method);
    return;
  }

  const incoming = new URL(request.url ?? "/", "http://frontend.invalid");
  const acceptsHtml = request.headers.accept?.includes("text/html") ?? false;
  const result = await findStaticFile(
    rootDirectory,
    incoming.pathname,
    acceptsHtml,
  );
  if (result.error) {
    sendText(
      response,
      result.error,
      result.error === 404 ? "Not found" : "Invalid path",
      method,
    );
    return;
  }

  const extension = extname(result.filePath).toLowerCase();
  const immutable = /\.[a-f0-9]{8,}\./i.test(result.filePath);
  response.writeHead(200, {
    "Cache-Control":
      extension === ".html" || result.spaFallback
        ? "no-cache"
        : immutable
          ? "public, max-age=31536000, immutable"
          : "public, max-age=3600",
    "Content-Length": result.fileInfo.size,
    "Content-Type": CONTENT_TYPES.get(extension) ?? "application/octet-stream",
    [PRODUCTION_HEADER]: "production",
  });
  if (method === "HEAD") {
    response.end();
    return;
  }
  createReadStream(result.filePath).pipe(response);
}

export function createFrontendArtifactServer({
  rootDirectory,
  apiOrigin = "http://127.0.0.1:5097",
}) {
  const root = resolve(rootDirectory);
  return createServer((request, response) => {
    const pathname = new URL(request.url ?? "/", "http://frontend.invalid")
      .pathname;
    if (pathname === "/api" || pathname.startsWith("/api/")) {
      proxyApiRequest(request, response, apiOrigin);
      return;
    }
    void serveStaticArtifact(request, response, root).catch((error) => {
      if (response.headersSent) {
        response.destroy(error);
        return;
      }
      sendText(
        response,
        500,
        `Static artifact server failed: ${error.message}`,
        request.method,
      );
    });
  });
}

async function main() {
  const rootDirectory = resolve(process.argv[2] ?? "dist");
  const rootInfo = await stat(rootDirectory);
  if (!rootInfo.isDirectory())
    throw new Error(`Artifact root is not a directory: ${rootDirectory}`);

  const host = process.env.FRONTEND_HOST ?? "127.0.0.1";
  const port = Number.parseInt(process.env.FRONTEND_PORT ?? "4173", 10);
  if (!Number.isInteger(port) || port < 1 || port > 65_535) {
    throw new Error(`Invalid FRONTEND_PORT: ${process.env.FRONTEND_PORT}`);
  }
  const apiOrigin = process.env.MOCK_API_ORIGIN ?? "http://127.0.0.1:5097";
  const server = createFrontendArtifactServer({ rootDirectory, apiOrigin });
  server.listen(port, host, () => {
    process.stdout.write(
      `Production frontend artifact listening on http://${host}:${port}; API proxy ${apiOrigin}\n`,
    );
  });
  const close = () => server.close();
  process.once("SIGINT", close);
  process.once("SIGTERM", close);
}

if (
  process.argv[1] &&
  fileURLToPath(import.meta.url) === resolve(process.argv[1])
) {
  try {
    await main();
  } catch (error) {
    process.stderr.write(`${error instanceof Error ? error.message : error}\n`);
    process.exitCode = 1;
  }
}
