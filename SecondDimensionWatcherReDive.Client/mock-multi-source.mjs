// Development-only source orchestration, sharing completion plans and the mock library.
import { planFor, submit } from "./mock-completion.mjs";

const subscriptions = new Map();
const decisions = new Map();
const iso = (timestamp) => new Date(timestamp).toISOString();

function evaluate(subscription, animations, downloadState) {
  const plan = planFor(animations, subscription.tmdbId, subscription.season);
  const prior = decisions.get(subscription.id) ?? [];
  const now = Date.now();
  const result = [];
  for (const episode of plan.episodes) {
    const linked = episode.candidates.filter((candidate) =>
      subscription.feedIds.includes(
        animations.get(candidate.releaseId)?.sourceFeedId,
      ),
    );
    if (!linked.length) continue;
    const old = prior.find((decision) => decision.episode === episode.episode);
    const firstSeen = Math.max(
      new Date(subscription.createdAt).getTime(),
      Math.min(
        ...linked.map((candidate) => {
          const release = animations.get(candidate.releaseId);
          return new Date(release.ingestedAt ?? release.publishTime).getTime();
        }),
      ),
    );
    const started = old?.waitStartedAt ?? iso(firstSeen);
    const waitUntil =
      new Date(started).getTime() + subscription.waitMinutes * 60000;
    const eligible = linked
      .filter((candidate) => candidate.eligible)
      .sort(
        (left, right) =>
          subscription.feedIds.indexOf(
            animations.get(left.releaseId).sourceFeedId,
          ) -
            subscription.feedIds.indexOf(
              animations.get(right.releaseId).sourceFeedId,
            ) ||
          right.score - left.score ||
          new Date(right.publishedAt) - new Date(left.publishedAt) ||
          left.releaseId.localeCompare(right.releaseId),
      );
    const current = episode.candidates.find(
      (candidate) => animations.get(candidate.releaseId).isDownloadFinished,
    );
    const downloading = episode.candidates.find((candidate) => {
      const release = animations.get(candidate.releaseId);
      return release.isDownloadTracked && !release.isDownloadFinished;
    });
    let selected = eligible[0];
    let outcome = "waiting";
    let reason = "waiting_for_primary";
    if (downloading || current) {
      selected = downloading ?? current;
      outcome = downloading ? "downloading" : "downloaded";
      reason = downloading
        ? "episode_already_downloading"
        : "existing_release_retained";
    } else if (!selected) {
      outcome = "unavailable";
      reason = "no_eligible_candidate";
    } else {
      const primary =
        animations.get(selected.releaseId).sourceFeedId ===
        subscription.feedIds[0];
      if (primary || now >= waitUntil) {
        reason = primary ? "primary_available" : "primary_wait_expired";
        outcome =
          subscription.mode === "NotifyOnly"
            ? "notified"
            : "pending_confirmation";
        if (subscription.mode === "AutoDownload") {
          const response = submit(
            animations,
            downloadState,
            subscription.tmdbId,
            subscription.season,
            [{ episode: episode.episode, releaseId: selected.releaseId }],
          )[0];
          outcome = response.isSuccess ? "downloading" : "failed";
          if (!response.isSuccess) reason = response.outcome;
        }
      }
    }
    result.push({
      subscriptionId: subscription.id,
      episode: episode.episode,
      waitStartedAt: started,
      waitUntil: iso(waitUntil),
      selectedReleaseId: selected?.releaseId ?? null,
      selectedTitle: selected?.title ?? null,
      selectedSourceFeedId: selected
        ? (animations.get(selected.releaseId).sourceFeedId ?? null)
        : null,
      outcome,
      reason,
      updatedAt: iso(now),
    });
  }
  decisions.set(subscription.id, result);
  return result;
}

export async function handleMultiSourceSubscriptions({
  req,
  res,
  method,
  pathname,
  json,
  readBody,
  animations,
  downloadState,
  feeds,
}) {
  const respond = (data, status = 200) => {
    json(res, data, status);
    return true;
  };
  if (pathname === "/api/multi-source-subscriptions" && method === "GET") {
    return respond(
      [...subscriptions.values()].map((subscription) => ({
        subscription,
        sources: subscription.feedIds.map((feedId, priority) => {
          const feed = feeds.find((x) => x.id === feedId);
          const latest = [...animations.values()]
            .filter((release) => release.sourceFeedId === feedId)
            .sort(
              (left, right) =>
                new Date(right.publishTime) - new Date(left.publishTime),
            )[0];
          return {
            feedId,
            name: feed?.name ?? feed?.url ?? feedId,
            priority,
            latestPublishedAt: latest?.publishTime ?? null,
            latestTitle: latest?.title ?? null,
            unidentifiedCount: 0,
          };
        }),
        decisions: decisions.get(subscription.id) ?? [],
      })),
    );
  }
  const match = pathname.match(
    /^\/api\/multi-source-subscriptions\/([^/]+)(?:\/(evaluate|episodes\/([0-9]+)\/confirm))?$/,
  );
  if (!match) return false;
  const id = match[1];
  if (method === "PUT" && !match[2]) {
    const body = await readBody(req);
    if (
      [...subscriptions.values()].some(
        (x) =>
          x.id !== id &&
          ((x.tmdbId === body.tmdbId && x.season === body.season) ||
            x.feedIds.some((feed) => body.feedIds.includes(feed))),
      )
    )
      return respond({ message: "Season or source already linked" }, 409);
    const subscription = {
      ...body,
      id,
      createdAt: subscriptions.get(id)?.createdAt ?? iso(Date.now()),
      updatedAt: iso(Date.now()),
    };
    const previous = subscriptions.get(id);
    if (
      previous &&
      (previous.tmdbId !== subscription.tmdbId ||
        previous.season !== subscription.season ||
        previous.feedIds.join(",") !== subscription.feedIds.join(","))
    )
      decisions.delete(id);
    subscriptions.set(id, subscription);
    evaluate(subscription, animations, downloadState);
    return respond(subscription);
  }
  if (method === "DELETE" && !match[2]) {
    const removed = subscriptions.delete(id);
    decisions.delete(id);
    return respond(null, removed ? 200 : 404);
  }
  const subscription = subscriptions.get(id);
  if (!subscription) return respond(null, 404);
  if (method === "POST" && match[2] === "evaluate")
    return respond(evaluate(subscription, animations, downloadState));
  if (method === "POST" && match[3]) {
    if (subscription.mode !== "ManualConfirm") return respond(null, 409);
    const decision = evaluate(subscription, animations, downloadState).find(
      (x) =>
        x.episode === Number(match[3]) && x.outcome === "pending_confirmation",
    );
    if (!decision) return respond(null, 409);
    const result = submit(
      animations,
      downloadState,
      subscription.tmdbId,
      subscription.season,
      [{ episode: decision.episode, releaseId: decision.selectedReleaseId }],
    )[0];
    evaluate(subscription, animations, downloadState);
    return respond(result);
  }
  return false;
}
