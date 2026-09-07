// Development data for native browser downloads.
import { randomBytes } from "node:crypto";

const grants = new Map();
const sessions = new Map();

export async function handleWatchlistPlayback(context) {
  const {
    req,
    res,
    method,
    pathname,
    searchParams,
    json,
    empty,
    profileId,
    session,
    liveSessions,
    vfsResolve,
  } = context;
  const reply = (data, status = 200) => {
    json(res, data, status);
    return true;
  };
  const done = (status = 204) => {
    empty(res, status);
    return true;
  };
  if (pathname === "/api/vfs/download-link" && method === "POST") {
    const path = searchParams.get("path");
    const entry = vfsResolve(path);
    if (!entry || entry.isDirectory) return done(404);
    const token = randomBytes(20).toString("hex");
    const sessionKey = `${session.id}:${profileId}`;
    const cookie = sessions.get(sessionKey) ?? randomBytes(20).toString("hex");
    sessions.set(sessionKey, cookie);
    grants.set(token, {
      path,
      sessionId: session.id,
      profileId,
      cookie,
      expires: Date.now() + 15 * 60000,
    });
    res.setHeader(
      "Set-Cookie",
      `sdw-mock-download=${cookie}; HttpOnly; SameSite=Strict; Path=/api/file/play/download`,
    );
    return reply({ url: `/api/file/play/download/${token}` });
  }
  if (
    pathname.startsWith("/api/file/play/download/") &&
    ["GET", "HEAD"].includes(method)
  ) {
    const grant = grants.get(pathname.split("/").pop());
    const live =
      grant &&
      [...liveSessions.values()].findLast((x) => x.id === grant.sessionId);
    if (
      !grant ||
      grant.expires <= Date.now() ||
      live?.profileId !== grant.profileId ||
      !req.headers.cookie
        ?.split("; ")
        .includes(`sdw-mock-download=${grant.cookie}`) ||
      !vfsResolve(grant.path)
    )
      return done(404);
    const bytes = Buffer.from(
      "Mock download. Production streams the original media without a browser Blob.\n",
    );
    const range = req.headers.range?.match(/^bytes=(\d*)-(\d*)$/);
    const start = range
      ? range[1]
        ? Number(range[1])
        : Math.max(0, bytes.length - Number(range[2]))
      : 0;
    const end =
      range && range[1] && range[2]
        ? Math.min(bytes.length - 1, Number(range[2]))
        : bytes.length - 1;
    if (start > end || start >= bytes.length) {
      res.setHeader("Content-Range", `bytes */${bytes.length}`);
      return done(416);
    }
    const filename = encodeURIComponent(grant.path.split("/").pop());
    const headers = {
      "Content-Type": "application/octet-stream",
      "Content-Disposition": `attachment; filename*=UTF-8''${filename}`,
      "Content-Length": end - start + 1,
      "Accept-Ranges": "bytes",
      "Cache-Control": "private,no-store",
      "Referrer-Policy": "no-referrer",
    };
    if (range)
      headers["Content-Range"] = `bytes ${start}-${end}/${bytes.length}`;
    res.writeHead(range ? 206 : 200, headers);
    res.end(method === "HEAD" ? undefined : bytes.subarray(start, end + 1));
    return true;
  }
  return false;
}
