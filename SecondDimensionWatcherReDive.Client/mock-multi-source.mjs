// Development-only source orchestration, sharing completion plans and the mock library.
import {
  executeUpgrade,
  isCurrentRelease,
  planFor,
  submit,
} from "./mock-completion.mjs";

const subscriptions = new Map();
const decisions = new Map();
const iso = (timestamp) => new Date(timestamp).toISOString();

export function multiSourcePolicyForFeed(feedId) {
  return [...subscriptions.values()].find((subscription) =>
    subscription.feedIds.includes(feedId),
  );
}

function evaluate(
  subscription,
  animations,
  downloadState,
  evaluateRelease,
  retryFailures = false,
) {
  // Evaluation is synchronous; only the currently stored snapshot can publish
  // decisions or trigger downloads for this subscription.
  if (subscriptions.get(subscription.id) !== subscription)
    return decisions.get(subscription.id) ?? [];
  const evaluateCandidate = (release) => evaluateRelease(release, subscription);
  const plan = planFor(
    animations,
    subscription.tmdbId,
    subscription.season,
    evaluateCandidate,
  );
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
    if (old?.outcome === "failed" && !retryFailures) {
      result.push(old);
      continue;
    }
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
    const current = episode.candidates.find((candidate) =>
      isCurrentRelease(animations.get(candidate.releaseId)),
    );
    const downloading = episode.candidates.find((candidate) => {
      const release = animations.get(candidate.releaseId);
      return release.isDownloadTracked && !release.isDownloadFinished;
    });
    let selected = eligible[0];
    let outcome = "waiting";
    let reason = "waiting_for_primary";
    if (downloading) {
      selected = downloading;
      const upgrading =
        animations.get(downloading.releaseId).upgradeOperation?.status ===
        "Downloading";
      outcome = upgrading ? "upgrading" : "downloading";
      reason = upgrading ? "download_queued" : "episode_already_downloading";
    } else if (current) {
      selected = current;
      outcome = "downloaded";
      reason = "existing_release_retained";
      if (
        subscription.mode === "AutoDownload" &&
        subscription.enableVersionUpgrade
      ) {
        const incumbent = animations.get(current.releaseId);
        const retainFallbackSource = subscription.feedIds
          .slice(1)
          .includes(incumbent.sourceFeedId);
        const upgrade = eligible
          .filter(
            (candidate) =>
              !retainFallbackSource ||
              animations.get(candidate.releaseId).sourceFeedId ===
                incumbent.sourceFeedId,
          )
          .sort((left, right) => right.score - left.score)[0];
        if (
          upgrade &&
          upgrade.score > current.score &&
          upgrade.score - current.score >= subscription.minimumUpgradeScore
        ) {
          const response = executeUpgrade(
            animations,
            downloadState,
            {
              currentReleaseId: current.releaseId,
              candidateReleaseId: upgrade.releaseId,
            },
            evaluateRelease,
            subscription,
            true,
          );
          selected = upgrade;
          outcome = response.isSuccess ? "upgrading" : "failed";
          reason = response.outcome;
        } else if (upgrade) reason = "upgrade_threshold_not_met";
      }
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
            evaluateCandidate,
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

export function removeMultiSourceFeed(feedId, animations) {
  for (const release of animations.values()) {
    if (release.sourceFeedId === feedId) release.sourceFeedId = null;
  }
  for (const subscription of subscriptions.values()) {
    if (!subscription.feedIds.includes(feedId)) continue;
    subscription.feedIds = subscription.feedIds.filter((id) => id !== feedId);
    subscription.updatedAt = iso(Date.now());
    if (!subscription.feedIds.length) {
      subscriptions.delete(subscription.id);
      decisions.delete(subscription.id);
      continue;
    }
    decisions.set(
      subscription.id,
      (decisions.get(subscription.id) ?? [])
        .filter((decision) =>
          [
            "downloaded",
            "downloading",
            "mapping_pending",
            "upgrading",
          ].includes(decision.outcome),
        )
        .map((decision) => ({
          ...decision,
          selectedSourceFeedId:
            animations.get(decision.selectedReleaseId)?.sourceFeedId ?? null,
        })),
    );
  }
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
  evaluateRelease,
  todoStates,
}) {
  const respond = (data, status = 200) => {
    json(res, data, status);
    return true;
  };
  if (pathname === "/api/multi-source-subscriptions" && method === "GET") {
    // The development UI's polling also drives deadline expiry. Normal evaluation
    // preserves terminal failures and already tracked downloads; it never requests a retry.
    for (const subscription of subscriptions.values())
      evaluate(subscription, animations, downloadState, evaluateRelease);
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
    const previous = subscriptions.get(id);
    const retargeted =
      previous &&
      (previous.tmdbId !== body.tmdbId || previous.season !== body.season);
    const savedAt = iso(Date.now());
    const subscription = {
      ...body,
      id,
      createdAt: retargeted ? savedAt : (previous?.createdAt ?? savedAt),
      updatedAt: savedAt,
    };
    if (
      previous &&
      (previous.tmdbId !== subscription.tmdbId ||
        previous.season !== subscription.season ||
        previous.feedIds.join(",") !== subscription.feedIds.join(","))
    )
      decisions.delete(id);
    subscriptions.set(id, subscription);
    const newlyLinked = subscription.feedIds.filter(
      (feedId) => !previous?.feedIds.includes(feedId),
    );
    for (const release of animations.values()) {
      if (
        newlyLinked.includes(release.sourceFeedId) &&
        !release.isDownloadTracked &&
        !release.isDownloadFinished &&
        ["Notified", "PendingConfirmation", "AutoDownloadFailed"].includes(
          release.automationDisposition,
        )
      ) {
        release.automationDisposition = null;
        release.automationExplanationJson = null;
        release.stateVersion = (release.stateVersion ?? 0) + 1;
        todoStates.delete(`automation:${release.id}`);
      }
    }
    evaluate(subscription, animations, downloadState, evaluateRelease);
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
    return respond(
      evaluate(subscription, animations, downloadState, evaluateRelease, true),
    );
  if (method === "POST" && match[3]) {
    if (subscription.mode !== "ManualConfirm") return respond(null, 409);
    const decision = evaluate(
      subscription,
      animations,
      downloadState,
      evaluateRelease,
    ).find(
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
      (release) => evaluateRelease(release, subscription),
    )[0];
    evaluate(subscription, animations, downloadState, evaluateRelease);
    return respond(result);
  }
  return false;
}
