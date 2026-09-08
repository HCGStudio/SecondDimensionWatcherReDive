// In-memory recognition rules for yarn dev. Saving never rewrites library items;
// historical application uses the mock review center's existing preview/apply flow.
import { randomUUID } from "node:crypto";

const rules = new Map();
const hits = [];
const normalize = (value) =>
  typeof value === "string" && value.trim() ? value.trim() : null;

function fail(code, message, status = 422) {
  throw Object.assign(new Error(message), { code, status });
}

function patternFor(pattern) {
  try {
    // Common .NET named captures also work in JavaScript. Advanced .NET-only
    // syntax is intentionally unavailable in this lightweight development server.
    return new RegExp(pattern, "gi");
  } catch {
    fail("rulePattern", "Use a valid regular expression with named captures.");
  }
}

function sourceFor(animation, feeds) {
  return (
    animation?.sourceFeedId ??
    feeds.find((feed) => feed.name === animation?.animation?.name)?.id ??
    null
  );
}

function matches(rule, item) {
  if (rule.sourceFeedId && rule.sourceFeedId !== item.sourceFeedId)
    return false;
  if (
    rule.subtitleGroup &&
    rule.subtitleGroup.toLowerCase() !== item.subtitleGroup?.toLowerCase()
  )
    return false;
  return !rule.titlePattern || patternFor(rule.titlePattern).test(item.title);
}

function resolve(rule, item) {
  const matches = rule.titlePattern
    ? [...item.title.matchAll(patternFor(rule.titlePattern))]
    : [];
  if (rule.titlePattern && matches.length !== 1)
    fail("ruleAmbiguous", "The rule must match exactly one title segment.");
  const capture = (name) => {
    const text = matches[0]?.groups?.[name];
    if (text == null) return null;
    const value = Number(text);
    if (
      !/^\d+$/.test(text) ||
      !Number.isSafeInteger(value) ||
      value > 2147483647
    )
      fail("ruleAmbiguous", "A season or episode capture is invalid.");
    return value;
  };
  const capturedSeason = capture("season");
  const capturedEpisode = capture("episode");
  const deterministic =
    capturedEpisode != null &&
    (rule.fixedSeason != null || capturedSeason != null);
  if (
    !deterministic &&
    (item.metadata.tmdbId !== rule.tmdbId ||
      (rule.fixedSeason != null && item.metadata.season !== rule.fixedSeason))
  )
    fail(
      "ruleAmbiguous",
      "The stored coordinates do not match this rule's target.",
    );
  const season = deterministic
    ? (rule.fixedSeason ?? capturedSeason)
    : item.metadata.season;
  const inputEpisode = deterministic ? capturedEpisode : item.metadata.episode;
  const offset =
    !deterministic && item.reviewStatus === "reviewed" ? 0 : rule.episodeOffset;
  const episode = inputEpisode == null ? null : inputEpisode + offset;
  if (
    (inputEpisode == null && rule.episodeOffset !== 0) ||
    (episode != null && (episode < 0 || episode > 2147483647))
  )
    fail(
      "ruleAmbiguous",
      "The episode offset cannot be applied unambiguously.",
    );
  return {
    tmdbId: rule.tmdbId,
    season,
    episode,
    groupName:
      rule.canonicalGroupName ?? item.metadata.groupName ?? item.subtitleGroup,
  };
}

function draftRule(body, id, context) {
  const current = id ? rules.get(id) : null;
  if (id && !current) fail("ruleNotFound", "Rule not found.", 404);
  if (current && body.expectedRevision !== current.revision)
    fail("ruleChanged", "Refresh the rule before saving or previewing.", 409);
  const name = normalize(body.name);
  const titlePattern = normalize(body.titlePattern);
  const subtitleGroup = normalize(body.subtitleGroup);
  const canonicalGroupName = normalize(body.canonicalGroupName);
  if (!name || name.length > 200) fail("ruleName", "A rule name is required.");
  if (!body.sourceFeedId && !titlePattern && !subtitleGroup)
    fail("ruleScope", "Restrict the source, title pattern, or subtitle group.");
  if (
    titlePattern?.length > 1000 ||
    subtitleGroup?.length > 200 ||
    canonicalGroupName?.length > 200
  )
    fail("ruleLength", "The pattern or group name is too long.");
  if (titlePattern && patternFor(titlePattern).test(""))
    fail("rulePattern", "The title pattern must not match empty text.");
  if (
    !/^\d+$/.test(body.tmdbId ?? "") ||
    Number(body.tmdbId) < 1 ||
    Number(body.tmdbId) > 2147483647
  )
    fail("invalidTmdbId", "Enter a positive TMDB ID.");
  if (
    (body.fixedSeason != null &&
      (!Number.isInteger(body.fixedSeason) ||
        body.fixedSeason < 0 ||
        body.fixedSeason > 2147483647)) ||
    !Number.isInteger(body.episodeOffset) ||
    Math.abs(body.episodeOffset) > 10000
  )
    fail("ruleNumbers", "Invalid season or episode offset.");
  const disablingOnly =
    current &&
    !body.enabled &&
    current.name === name &&
    current.sourceFeedId === (body.sourceFeedId || null) &&
    current.titlePattern === titlePattern &&
    current.subtitleGroup === subtitleGroup &&
    current.tmdbId === String(Number(body.tmdbId)) &&
    current.fixedSeason === (body.fixedSeason ?? null) &&
    current.episodeOffset === body.episodeOffset &&
    current.canonicalGroupName === canonicalGroupName;
  if (
    !disablingOnly &&
    !context.metadataCatalog.has(String(Number(body.tmdbId)))
  )
    fail("tmdbNotFound", "The TMDB series is not in the mock catalog.");
  if (!disablingOnly && body.fixedSeason != null) {
    const seasons = context.metadataCatalog.get(
      String(Number(body.tmdbId)),
    ).seasonNumbers;
    if (!Array.isArray(seasons))
      fail(
        "tmdbUnavailable",
        "The mock catalog's season data is unavailable.",
        503,
      );
    if (!seasons.includes(body.fixedSeason))
      fail(
        "ruleSeasonNotFound",
        "The fixed season is not in the mock series catalog.",
      );
  }
  if (
    !disablingOnly &&
    body.sourceFeedId &&
    !context.feeds.some((feed) => feed.id === body.sourceFeedId)
  )
    fail("ruleSource", "The subscription source no longer exists.");
  if (
    !id &&
    body.createdFromItemId &&
    context.metadataReviewItems.get(body.createdFromItemId)?.reviewStatus !==
      "reviewed"
  )
    fail("ruleCorrection", "Complete the correction before creating its rule.");
  const now = new Date().toISOString();
  return {
    id: id ?? randomUUID(),
    name,
    enabled: Boolean(body.enabled),
    revision: (current?.revision ?? 0) + 1,
    sourceFeedId: body.sourceFeedId || null,
    titlePattern,
    subtitleGroup,
    tmdbId: String(Number(body.tmdbId)),
    fixedSeason: body.fixedSeason ?? null,
    episodeOffset: body.episodeOffset,
    canonicalGroupName,
    createdFromItemId:
      current?.createdFromItemId ?? body.createdFromItemId ?? null,
    createdAt: current?.createdAt ?? now,
    effectiveFrom: now,
  };
}

function candidates(context) {
  return [...context.animations.values()]
    .map((animation, index) => {
      const files = context.mockMappedFiles(animation, index);
      const review = context.metadataReviewItems.get(animation.id) ?? {
        id: animation.id,
        title: animation.title,
        description: animation.description,
        publishTime: animation.publishTime,
        revision: 1,
        reviewStatus: animation.animation ? "identified" : "pending",
        confidence: animation.animation ? 1 : null,
        failureReason: null,
        aiRetryCount: 0,
        currentOperationId: null,
        isDownloadFinished: animation.isDownloadFinished,
        mappedFileCount: files.length,
        files,
        metadata: {
          tmdbId: animation.animation?.tmdbId ?? null,
          name: animation.animation?.name ?? null,
          originalName: animation.animation?.originalName ?? null,
          posterPath: animation.animation?.posterPath ?? null,
          season: animation.season ?? null,
          episode: animation.episode ?? null,
          groupName: animation.group?.name ?? null,
        },
      };
      return {
        ...review,
        sourceFeedId: sourceFor(animation, context.feeds),
        subtitleGroup:
          animation.releaseSubtitleGroup ?? animation.group?.name ?? null,
      };
    })
    .sort((a, b) => Date.parse(b.publishTime) - Date.parse(a.publishTime));
}

export function isMetadataRulePreviewCurrent(preview, item, animation, feeds) {
  if (!preview.recognitionRuleId) return true;
  const rule = rules.get(preview.recognitionRuleId);
  if (!rule?.enabled || rule.revision !== preview.recognitionRuleRevision)
    return false;
  const current = {
    ...item,
    sourceFeedId: sourceFor(animation, feeds),
    subtitleGroup:
      animation?.releaseSubtitleGroup ?? animation?.group?.name ?? null,
  };
  return (
    matches(rule, current) &&
    ![...rules.values()].some(
      (other) =>
        other.enabled && other.id !== rule.id && matches(other, current),
    )
  );
}

export async function handleMetadataRules(context) {
  const { req, res, method, pathname, searchParams, json, readBody } = context;
  if (!pathname.startsWith("/api/metadata-rules")) return false;
  const reply = (body, status = 200) => {
    json(res, body, status);
    return true;
  };
  try {
    if (pathname === "/api/metadata-rules" && method === "GET")
      return reply(
        [...rules.values()].sort((a, b) =>
          b.createdAt.localeCompare(a.createdAt),
        ),
      );
    if (pathname === "/api/metadata-rules/hits" && method === "GET") {
      const itemId = searchParams.get("itemId");
      // There is no background AI worker in the mock; saves and historical
      // corrections must not fabricate automatic-recognition provenance.
      return reply(
        hits
          .filter((hit) => !itemId || hit.animationInfoId === itemId)
          .slice(0, 50),
      );
    }
    const seedMatch = pathname.match(
      /^\/api\/metadata-rules\/from-correction\/([^/]+)$/,
    );
    if (method === "GET" && seedMatch) {
      const item = context.metadataReviewItems.get(seedMatch[1]);
      if (!item) fail("itemNotFound", "Item not found.", 404);
      if (item.reviewStatus !== "reviewed")
        fail("ruleCorrection", "Complete this correction first.");
      const animation = context.animations.get(item.id);
      return reply({
        itemId: item.id,
        title: item.title,
        sourceFeedId: sourceFor(animation, context.feeds),
        subtitleGroup:
          animation?.releaseSubtitleGroup ?? animation?.group?.name ?? null,
        tmdbId: item.metadata.tmdbId,
        season: item.metadata.season,
        groupName: item.metadata.groupName,
      });
    }
    const historyMatch = pathname.match(
      /^\/api\/metadata-rules\/([^/]+)\/history\/([^/]+)\/preview$/,
    );
    if (method === "POST" && historyMatch) {
      const rule = rules.get(historyMatch[1]);
      if (!rule) fail("ruleNotFound", "Rule not found.", 404);
      const body = await readBody(req);
      if (!rule.enabled || body.ruleRevision !== rule.revision)
        fail("ruleChanged", "Refresh the rule preview.", 409);
      const item = candidates(context).find(
        (candidate) => candidate.id === historyMatch[2],
      );
      if (!item) fail("itemNotFound", "Item not found.", 404);
      if (body.itemRevision !== item.revision)
        fail("ruleChanged", "Refresh this item's revision.", 409);
      if (
        !matches(rule, item) ||
        [...rules.values()].some(
          (other) =>
            other.enabled && other.id !== rule.id && matches(other, item),
        )
      )
        fail(
          "ruleConflict",
          "Matching changed or overlaps another enabled rule.",
          409,
        );
      const resolved = resolve(rule, item);
      if (resolved.season == null)
        fail("ruleAmbiguous", "Identify the season before applying this rule.");
      const identity = context.metadataCatalog.get(rule.tmdbId) ?? {
        tmdbId: rule.tmdbId,
        name: `TMDB ${rule.tmdbId}`,
        originalName: null,
        posterPath: null,
      };
      const resolvedMetadata = { ...identity, ...resolved };
      const preview = {
        previewId: randomUUID(),
        recognitionRuleId: rule.id,
        recognitionRuleRevision: rule.revision,
        itemId: item.id,
        baseRevision: item.revision,
        resolvedMetadata,
        pathChanges: context.buildMetadataPathChanges(item, resolvedMetadata),
        warnings: item.isDownloadFinished ? [] : ["notDownloaded"],
        canApply: true,
        expiresAt: new Date(Date.now() + 15 * 60000).toISOString(),
      };
      // Materialize previously identified mock items only for an explicit historical
      // preview, so the existing apply and undo endpoints can operate on the same record.
      if (!context.metadataReviewItems.has(item.id))
        context.metadataReviewItems.set(item.id, item);
      context.metadataReviewPreviews.set(preview.previewId, preview);
      const { itemId: _itemId, ...response } = preview;
      return reply(response);
    }
    if (method === "POST" && pathname === "/api/metadata-rules/preview") {
      const id = searchParams.get("id");
      const rule = draftRule(await readBody(req), id, context);
      const all = candidates(context).filter(
        (item) => !rule.sourceFeedId || item.sourceFeedId === rule.sourceFeedId,
      );
      const scanned = all.slice(0, 1000);
      const matched = scanned.filter((item) => matches(rule, item));
      const samples = matched.slice(0, 50).map((item) => {
        let warning = [...rules.values()].some(
          (other) => other.enabled && other.id !== id && matches(other, item),
        )
          ? "conflict"
          : null;
        let resolved = {
          tmdbId: rule.tmdbId,
          season: null,
          episode: null,
          groupName: null,
        };
        try {
          resolved = resolve(rule, item);
          if (resolved.season == null) warning ??= "needsInference";
        } catch (error) {
          if (error.code !== "ruleAmbiguous") throw error;
          warning = "ambiguous";
        }
        return {
          itemId: item.id,
          title: item.title,
          revision: item.revision,
          currentTmdbId: item.metadata.tmdbId,
          currentSeason: item.metadata.season,
          currentEpisode: item.metadata.episode,
          currentGroupName: item.metadata.groupName,
          ...resolved,
          warning,
        };
      });
      return reply({
        scannedCount: scanned.length,
        matchCount: matched.length,
        truncated: all.length > 1000,
        samples,
      });
    }
    const editMatch = pathname.match(/^\/api\/metadata-rules\/([^/]+)$/);
    if (
      (method === "POST" && pathname === "/api/metadata-rules") ||
      (method === "PUT" && editMatch)
    ) {
      const id = method === "PUT" ? editMatch[1] : null;
      const rule = draftRule(await readBody(req), id, context);
      if (!id && rules.size >= 200)
        fail("ruleLimit", "Up to 200 rules are supported.");
      rules.set(rule.id, rule);
      return reply(rule);
    }
    return false;
  } catch (error) {
    if (!error.code || !error.status) throw error;
    return reply({ code: error.code, message: error.message }, error.status);
  }
}
