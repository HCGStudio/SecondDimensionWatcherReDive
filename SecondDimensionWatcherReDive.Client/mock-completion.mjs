// Development-only completion plans, sharing the mock library.
const iso = (timestamp) => new Date(timestamp).toISOString();
const date = (timestamp) => iso(timestamp).slice(0, 10);

export function planFor(animations, tmdbId, season) {
  const releases = [...animations.values()].filter(
    (x) => x.animation?.tmdbId === tmdbId && x.season === season,
  );
  const maximum = Math.max(12, ...releases.map((x) => x.episode ?? 0));
  const now = Date.now();
  const episodes = Array.from({ length: maximum + 2 }, (_, index) => {
    const episode = index + 1;
    const candidates = releases
      .filter((x) => x.episode === episode)
      .map((x) => ({
        releaseId: x.id,
        title: x.title,
        publishedAt: x.publishTime,
        sizeBytes: x.releaseSizeBytes ?? null,
        score: x.title.includes("2160") ? 480 : 280,
        reasons: ["resolution:1080p:+200", "codec:HEVC:+60"],
        eligible: !x.isDownloadTracked && !x.isDownloadFinished,
        unavailableReason: x.isDownloadFinished
          ? "downloaded"
          : x.isDownloadTracked
            ? "downloading"
            : null,
      }));
    const downloaded = candidates.some(
      (x) => animations.get(x.releaseId).isDownloadFinished,
    );
    const downloading = candidates.some(
      (x) => animations.get(x.releaseId).isDownloadTracked,
    );
    const unaired = episode === maximum + 1;
    const airDate =
      episode === maximum + 2
        ? null
        : date(now + (unaired ? 7 : episode - maximum - 1) * 86400000);
    const state = downloaded
      ? "downloaded"
      : downloading
        ? "downloading"
        : unaired
          ? "unaired"
          : candidates.length
            ? "candidate"
            : airDate == null
              ? "air_date_unknown"
              : "aired_no_resource";
    return {
      episode,
      state,
      airDate,
      candidates,
      selectedReleaseId:
        state === "candidate"
          ? (candidates.find((x) => x.eligible)?.releaseId ?? null)
          : null,
      reason: state === "candidate" ? "highest_eligible_score" : state,
    };
  });
  return {
    tmdbId,
    animationName: releases[0]?.animation?.name ?? tmdbId,
    season,
    generatedAt: iso(now),
    airDatesCheckedAt: iso(now),
    airDatesSource: "Mock",
    unidentifiedReleaseCount: 0,
    episodes,
  };
}

export function submit(animations, downloadState, tmdbId, season, selections) {
  return selections.map((selection) => {
    const episode = planFor(animations, tmdbId, season).episodes.find(
      (x) => x.episode === selection.episode,
    );
    const item = animations.get(selection.releaseId);
    if (episode?.state === "downloaded" || episode?.state === "downloading")
      return {
        ...selection,
        isSuccess: true,
        outcome: "already_present_or_busy",
      };
    if (
      !item ||
      episode?.state === "unaired" ||
      !episode?.candidates.some(
        (x) => x.releaseId === selection.releaseId && x.eligible,
      )
    )
      return {
        ...selection,
        isSuccess: false,
        outcome: "candidate_unavailable",
      };
    item.isDownloadTracked = true;
    item.automationDisposition = "AutoDownloadQueued";
    downloadState.set(item.id, {
      state: "Downloading",
      progress: 0,
      startedAt: Date.now(),
    });
    return { ...selection, isSuccess: true, outcome: "submitted" };
  });
}

export async function handleCompletion({
  req,
  res,
  method,
  pathname,
  searchParams,
  json,
  readBody,
  animations,
  downloadState,
}) {
  const respond = (data, status = 200) => {
    json(res, data, status);
    return true;
  };
  if (pathname === "/api/library/completion") {
    if (method === "GET")
      return respond(
        planFor(
          animations,
          searchParams.get("tmdbId"),
          Number(searchParams.get("season")),
        ),
      );
    if (method === "POST") {
      const body = await readBody(req);
      return respond(
        submit(
          animations,
          downloadState,
          body.tmdbId,
          body.season,
          body.selections ?? [],
        ),
      );
    }
  }
  return false;
}
