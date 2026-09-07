// Development data for personal watchlists, shared timestamps and native downloads.
import { createHash, randomBytes, randomUUID } from "node:crypto";

const lists = new Map();
const timelines = new Map();
const accepted = new Map();
const grants = new Map();
const sessions = new Map();
const statuses = new Set([
  "planned",
  "watching",
  "onHold",
  "dropped",
  "completed",
]);

export async function handleWatchlistPlayback(context) {
  const {
    req,
    res,
    method,
    pathname,
    searchParams,
    json,
    empty,
    readBody,
    animations,
    seasonBangumis,
    profileId,
    session,
    liveSessions,
    vfsResolve,
    playbackProgress,
    playbackKey,
    playablePaths,
  } = context;
  const reply = (data, status = 200) => {
    json(res, data, status);
    return true;
  };
  const done = (status = 204) => {
    empty(res, status);
    return true;
  };
  const list = lists.get(profileId) ?? new Map();
  lists.set(profileId, list);
  if (pathname === "/api/watchlist" && method === "GET") {
    const start = new Date(
      searchParams.get("weekStart") ?? Date.now(),
    ).getTime();
    const end = start + 7 * 86400000;
    return reply(
      [...list.values()].map((item) => ({
        ...item,
        dayOfWeek: (() => {
          const day = seasonBangumis.find(
            (x) => x.mikanId === item.mikanId,
          )?.dayOfWeek;
          return day >= 0 && day <= 6 ? day : null;
        })(),
        episodes: [...animations.values()]
          .filter((x) => x.animation?.tmdbId === item.tmdbId)
          .map((x) => {
            const path = playablePaths()[0];
            const state = playbackProgress.get(playbackKey(x.id, path));
            return state?.isWatched
              ? null
              : {
                  animationInfoId: x.id,
                  title: x.title,
                  season: x.season,
                  episode: x.episode,
                  publishedAt: x.publishTime,
                  availability: x.isDownloadFinished
                    ? "downloaded"
                    : "released",
                  path: x.isDownloadFinished ? path : null,
                  positionSeconds: state?.positionSeconds ?? 0,
                };
          })
          .filter(
            (x) =>
              x &&
              (x.path ||
                (new Date(x.publishedAt).getTime() >= start &&
                  new Date(x.publishedAt).getTime() < end)),
          ),
      })),
    );
  }
  if (pathname === "/api/watchlist" && method === "PUT") {
    const body = await readBody(req);
    if (
      !statuses.has(body.status) ||
      typeof body.title !== "string" ||
      !body.title.trim() ||
      (body.mikanId != null &&
        (!Number.isInteger(body.mikanId) || body.mikanId <= 0))
    )
      return done(400);
    if (body.tmdbId != null) {
      if (
        typeof body.tmdbId !== "string" ||
        !/^\+?\d+$/.test(body.tmdbId.trim())
      )
        return done(400);
      const tmdbId = BigInt(body.tmdbId.trim());
      if (tmdbId <= 0n || tmdbId > 9223372036854775807n) return done(400);
      body.tmdbId = tmdbId.toString();
    }
    const target = body.id != null ? list.get(body.id) : undefined;
    if (body.id != null && !target) return done(404);
    const tmdbId = Object.hasOwn(body, "tmdbId")
      ? body.tmdbId
      : target?.tmdbId;
    const mikanId = Object.hasOwn(body, "mikanId")
      ? body.mikanId
      : target?.mikanId;
    if (tmdbId == null && mikanId == null) return done(400);
    const matching = [...list.values()].filter(
      (x) =>
        (tmdbId != null && x.tmdbId === tmdbId) ||
        (mikanId != null && x.mikanId === mikanId),
    );
    const previous = target ?? matching[0];
    for (const item of matching) list.delete(item.id);
    const item = {
      ...previous,
      ...body,
      id: previous?.id ?? randomUUID(),
      tmdbId: Object.hasOwn(body, "tmdbId")
        ? body.tmdbId
        : (previous?.tmdbId ?? null),
      mikanId: Object.hasOwn(body, "mikanId")
        ? body.mikanId
        : (previous?.mikanId ?? null),
      updatedAt: new Date().toISOString(),
    };
    list.set(item.id, item);
    return done();
  }
  if (pathname.startsWith("/api/watchlist/") && method === "DELETE")
    return done(list.delete(pathname.split("/").pop()) ? 204 : 404);

  if (pathname.startsWith("/api/playback/timeline")) {
    const body = ["PUT", "POST"].includes(method)
      ? await readBody(req)
      : Object.fromEntries(searchParams);
    const animation = animations.get(body.animationInfoId);
    if (!animation?.isDownloadFinished || !playablePaths().includes(body.path))
      return done(404);
    const mediaVersion = createHash("sha256")
      .update(`${animation.id}:${body.path}`)
      .digest("hex");
    const seasonKey =
      animation.animation && animation.group?.name && animation.season != null
        ? JSON.stringify([
            "season",
            animation.animation.tmdbId,
            animation.group.name,
            animation.season,
          ])
        : null;
    const episodeKey = `media:${mediaVersion}`;
    const season = timelines.get(seasonKey) ?? null;
    if (method === "GET")
      return reply({
        mediaVersion,
        seasonKey,
        episode: timelines.get(episodeKey) ?? null,
        seasonDefault: season,
        seasonAccepted: Boolean(
          season && accepted.get(mediaVersion) === season.updatedAt,
        ),
      });
    if (body.mediaVersion !== mediaVersion) return done(409);
    const duration = Number(body.durationSeconds);
    if (method === "POST") {
      if (
        !season ||
        !Number.isFinite(duration) ||
        duration <= 0 ||
        season.points.some(
          (x) => x.endSeconds > duration || x.startSeconds >= duration,
        )
      )
        return done(400);
      accepted.set(mediaVersion, season.updatedAt);
      return done();
    }
    const defaultScope =
      body.seasonDefault === true || body.seasonDefault === "true";
    const key = defaultScope ? seasonKey : episodeKey;
    if (!key) return done(400);
    if (method === "DELETE") {
      timelines.delete(key);
      return done();
    }
    if (method === "PUT") {
      const points = body.points;
      if (
        !Number.isFinite(duration) ||
        duration <= 0 ||
        !Array.isArray(points) ||
        points.length > 200 ||
        points.some(
          (x) =>
            !x?.name?.trim() ||
            !["opening", "ending", "chapter"].includes(x.kind) ||
            !Number.isFinite(x.startSeconds) ||
            !Number.isFinite(x.endSeconds) ||
            x.startSeconds < 0 ||
            x.startSeconds >= duration ||
            x.endSeconds > duration ||
            x.endSeconds < x.startSeconds ||
            (x.kind !== "chapter" && x.startSeconds === x.endSeconds),
        )
      )
        return done(400);
      const enabled = points
        .filter((x) => x.enabled && x.kind !== "chapter")
        .sort((a, b) => a.startSeconds - b.startSeconds);
      if (
        new Set(enabled.map((x) => x.kind)).size !== enabled.length ||
        enabled.some(
          (x, i) => i > 0 && enabled[i - 1].endSeconds > x.startSeconds,
        )
      )
        return done(400);
      const updatedAt = new Date().toISOString();
      timelines.set(key, { key, durationSeconds: duration, points, updatedAt });
      if (defaultScope) accepted.set(mediaVersion, updatedAt);
      return done();
    }
  }

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
