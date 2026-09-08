// Development-only completion plans, sharing the mock library.
import { randomUUID } from "node:crypto";

const iso = (timestamp) => new Date(timestamp).toISOString();
const date = (timestamp) => iso(timestamp).slice(0, 10);

export function isCurrentRelease(release) {
  return release.isDownloadFinished && !release.supersededByReleaseId;
}

export function planFor(animations, tmdbId, season, evaluateRelease) {
  const releases = [...animations.values()].filter(
    (x) => x.animation?.tmdbId === tmdbId && x.season === season,
  );
  const maximum = Math.max(12, ...releases.map((x) => x.episode ?? 0));
  const now = Date.now();
  const episodes = Array.from({ length: maximum + 2 }, (_, index) => {
    const episode = index + 1;
    const candidates = releases
      .filter((x) => x.episode === episode)
      .map((x) => {
        const evaluation = evaluateRelease(x);
        const unavailableReason = x.isDownloadFinished
          ? "downloaded"
          : x.isDownloadTracked
            ? "downloading"
            : !evaluation.matched
              ? "policy_mismatch"
              : null;
        return {
          releaseId: x.id,
          title: x.title,
          publishedAt: x.publishTime,
          sizeBytes: x.releaseSizeBytes ?? null,
          score: evaluation.score,
          reasons: evaluation.scoreReasons,
          eligible: unavailableReason == null,
          unavailableReason,
        };
      })
      .sort(
        (left, right) =>
          Number(right.eligible) - Number(left.eligible) ||
          right.score - left.score ||
          new Date(right.publishedAt) - new Date(left.publishedAt) ||
          left.releaseId.localeCompare(right.releaseId),
      );
    const downloaded = candidates.some((x) =>
      isCurrentRelease(animations.get(x.releaseId)),
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
        state === "candidate" && airDate !== null
          ? (candidates.find((x) => x.eligible)?.releaseId ?? null)
          : null,
      reason:
        state === "candidate"
          ? airDate === null
            ? "air_date_unknown"
            : "highest_eligible_score"
          : state,
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

export function submit(
  animations,
  downloadState,
  tmdbId,
  season,
  selections,
  evaluateRelease,
) {
  return selections.map((selection) => {
    const episode = planFor(
      animations,
      tmdbId,
      season,
      evaluateRelease,
    ).episodes.find((x) => x.episode === selection.episode);
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

// Both explicit upgrades and multi-source automation use the same synchronous
// claim, download progress and activation path. Old media remains downloadable.
export function executeUpgrade(
  animations,
  downloadState,
  request,
  evaluateRelease,
  policy,
  automatic = false,
) {
  const current = animations.get(request.currentReleaseId);
  const candidate = animations.get(request.candidateReleaseId);
  const dryRun = !!request.dryRun;
  const requiresDownload = !!candidate && !candidate.isDownloadFinished;
  const result = (isSuccess, outcome, operation = null) => ({
    isSuccess,
    outcome,
    dryRun,
    requiresDownload,
    operation,
    validationErrors: isSuccess ? [] : [outcome],
  });
  if (
    !current ||
    !candidate ||
    current.id === candidate.id ||
    !isCurrentRelease(current) ||
    candidate.supersededByReleaseId ||
    !current.animation?.tmdbId ||
    current.animation.tmdbId !== candidate.animation?.tmdbId ||
    current.season !== candidate.season ||
    current.episode !== candidate.episode ||
    !(current.season > 0 && current.episode > 0)
  )
    return result(false, "candidate_unavailable");
  const previousScore = evaluateRelease(current, policy).score;
  const next = evaluateRelease(candidate, policy);
  if (
    next.score <= previousScore ||
    (automatic &&
      (policy?.mode !== "AutoDownload" ||
        !policy.enableVersionUpgrade ||
        !next.matched ||
        next.score - previousScore < policy.minimumUpgradeScore ||
        !policy.feedIds.includes(candidate.sourceFeedId) ||
        (policy.feedIds.slice(1).includes(current.sourceFeedId) &&
          candidate.sourceFeedId !== current.sourceFeedId)))
  )
    return result(false, "upgrade_threshold_not_met");
  if (
    [...animations.values()].some(
      (release) =>
        release.animation?.tmdbId === current.animation.tmdbId &&
        release.season === current.season &&
        release.episode === current.episode &&
        release.isDownloadTracked &&
        !release.isDownloadFinished,
    )
  )
    return result(false, "upgrade_already_started");
  if (dryRun) return result(true, "ready");
  const operation = {
    id: randomUUID(),
    currentReleaseId: current.id,
    candidateReleaseId: candidate.id,
    status: requiresDownload ? "Downloading" : "Verifying",
    currentScore: previousScore,
    candidateScore: next.score,
    createdAt: iso(Date.now()),
    appliedAt: null,
    rollbackUntil: null,
  };
  candidate.upgradeOperation = operation;
  candidate.upgradeRollbackHours = policy?.upgradeRollbackHours ?? 72;
  if (!requiresDownload) {
    completeUpgrade(animations, candidate);
    return result(true, "applied", operation);
  }
  candidate.isDownloadTracked = true;
  candidate.automationDisposition = automatic
    ? "AutoDownloadQueued"
    : "ManualDownloadQueued";
  downloadState.set(candidate.id, {
    state: "Downloading",
    progress: 0,
    startedAt: Date.now(),
  });
  return result(true, "download_queued", operation);
}

export function completeUpgrade(animations, candidate) {
  const operation = candidate.upgradeOperation;
  if (!operation || !["Downloading", "Verifying"].includes(operation.status))
    return;
  const previous = animations.get(operation.currentReleaseId);
  if (previous) previous.supersededByReleaseId = candidate.id;
  operation.status = "Applied";
  operation.appliedAt = iso(Date.now());
  operation.rollbackUntil = iso(
    Date.now() + candidate.upgradeRollbackHours * 3600000,
  );
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
  evaluateRelease,
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
          evaluateRelease,
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
          evaluateRelease,
        ),
      );
    }
  }
  return false;
}
