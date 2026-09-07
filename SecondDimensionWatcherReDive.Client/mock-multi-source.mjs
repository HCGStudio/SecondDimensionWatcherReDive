// Development-only source orchestration, sharing completion plans and the mock library.
import { planFor, submit } from "./mock-completion.mjs";

const subscriptions = new Map();
const decisions = new Map();
const iso = (timestamp) => new Date(timestamp).toISOString();

function evaluate(subscription, animations, downloadState) {
  const plan = planFor(animations, subscription.tmdbId, subscription.season);
  const prior = decisions.get(subscription.id) ?? [];
  const now = Date.now();
  const result = plan.episodes
    .filter((x) => x.candidates.length)
    .map((episode) => {
      const old = prior.find((x) => x.episode === episode.episode);
      const selected =
        episode.candidates.find(
          (x) => x.releaseId === episode.selectedReleaseId,
        ) ?? episode.candidates[0];
      const started = old?.waitStartedAt ?? subscription.createdAt;
      let outcome =
        episode.state === "downloaded"
          ? "downloaded"
          : episode.state === "downloading"
            ? "downloading"
            : subscription.mode === "NotifyOnly"
              ? "notified"
              : "pending_confirmation";
      if (subscription.mode === "AutoDownload" && episode.selectedReleaseId) {
        const response = submit(
          animations,
          downloadState,
          subscription.tmdbId,
          subscription.season,
          [{ episode: episode.episode, releaseId: episode.selectedReleaseId }],
        )[0];
        outcome = response.isSuccess ? "downloading" : "failed";
      }
      return {
        subscriptionId: subscription.id,
        episode: episode.episode,
        waitStartedAt: started,
        waitUntil: iso(
          new Date(started).getTime() + subscription.waitMinutes * 60000,
        ),
        selectedReleaseId: selected.releaseId,
        selectedTitle: selected.title,
        selectedSourceFeedId: subscription.feedIds[0] ?? null,
        outcome,
        reason:
          outcome === "downloaded"
            ? "existing_release_retained"
            : outcome === "downloading"
              ? "episode_already_downloading"
              : "primary_available",
        updatedAt: iso(now),
      };
    });
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
          const latest = [...animations.values()].find(
            (x) => x.animation?.tmdbId === subscription.tmdbId,
          );
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
