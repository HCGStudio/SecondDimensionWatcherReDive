import { createServer } from "node:http";

const port = Number.parseInt(process.env.SDW_FUSE_SMOKE_PORT ?? "15097", 10);
const expectedAuthorization = `Basic ${Buffer.from("smoke-user:smoke-token").toString("base64")}`;
const contents = Buffer.from("sdwfuse mount smoke\n", "utf8");

function json(response, value, status = 200) {
  response.writeHead(status, { "Content-Type": "application/json" });
  response.end(JSON.stringify(value));
}

function entry(path) {
  if (path === "/")
    return { name: "", isDirectory: true, size: null, lastModifiedUtc: null };
  if (path === "/library")
    return {
      name: "library",
      isDirectory: true,
      size: null,
      lastModifiedUtc: null,
    };
  if (path === "/library/probe.txt")
    return {
      name: "probe.txt",
      isDirectory: false,
      size: contents.length,
      lastModifiedUtc: "2026-08-29T00:00:00Z",
    };
  return null;
}

const server = createServer((request, response) => {
  if (request.headers.authorization !== expectedAuthorization) {
    response.writeHead(401, { "WWW-Authenticate": 'Basic realm="smoke"' });
    response.end();
    return;
  }

  const url = new URL(request.url ?? "/", `http://127.0.0.1:${port}`);
  const path = url.searchParams.get("path") ?? "/";
  if (url.pathname === "/api/vfs/stat") {
    const result = entry(path);
    if (result) json(response, result);
    else response.writeHead(404).end();
    return;
  }

  if (url.pathname === "/api/vfs/list") {
    if (path === "/") json(response, [entry("/library")]);
    else if (path === "/library") json(response, [entry("/library/probe.txt")]);
    else response.writeHead(404).end();
    return;
  }

  if (url.pathname === "/api/vfs/read" && path === "/library/probe.txt") {
    const range = request.headers.range?.match(/^bytes=(\d+)-(\d+)$/);
    if (!range) {
      response.writeHead(200, { "Content-Length": contents.length });
      response.end(contents);
      return;
    }

    const start = Number.parseInt(range[1], 10);
    const end = Math.min(Number.parseInt(range[2], 10), contents.length - 1);
    if (start >= contents.length || end < start) {
      response.writeHead(416, { "Content-Range": `bytes */${contents.length}` });
      response.end();
      return;
    }
    const slice = contents.subarray(start, end + 1);
    response.writeHead(206, {
      "Accept-Ranges": "bytes",
      "Content-Length": slice.length,
      "Content-Range": `bytes ${start}-${end}/${contents.length}`,
    });
    response.end(slice);
    return;
  }

  response.writeHead(404).end();
});

server.listen(port, "127.0.0.1", () => {
  process.stdout.write(`FUSE smoke server listening on ${port}\n`);
});

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => server.close(() => process.exit(0)));
}
