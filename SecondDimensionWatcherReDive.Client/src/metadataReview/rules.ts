import fetcher from "../auth/httpClient";

export interface RecognitionRuleDraft {
  name: string;
  enabled: boolean;
  sourceFeedId: string | null;
  titlePattern: string | null;
  subtitleGroup: string | null;
  tmdbId: string;
  fixedSeason: number | null;
  episodeOffset: number;
  canonicalGroupName: string | null;
  createdFromItemId: string | null;
  expectedRevision: number | null;
}

export interface RecognitionRule extends RecognitionRuleDraft {
  id: string;
  revision: number;
  effectiveFrom: string;
}

export interface RecognitionSample {
  itemId: string;
  title: string;
  revision: number;
  currentTmdbId: string | null;
  currentSeason: number | null;
  currentEpisode: number | null;
  currentGroupName: string | null;
  tmdbId: string;
  season: number | null;
  episode: number | null;
  groupName: string | null;
  warning: string | null;
}

export interface RecognitionPreview {
  scannedCount: number;
  matchCount: number;
  truncated: boolean;
  samples: RecognitionSample[];
}

export interface RecognitionSeed {
  itemId: string;
  title: string;
  sourceFeedId: string | null;
  subtitleGroup: string | null;
  tmdbId: string | null;
  season: number | null;
  groupName: string | null;
}

export interface RecognitionHit {
  id: string;
  ruleId: string;
  ruleName: string;
  ruleRevision: number;
  animationInfoId: string;
  title: string;
  itemRevision: number;
  appliedAt: string;
}

export function ruleRequest<T>(
  url: string,
  body: unknown,
  method = "POST",
): Promise<T> {
  return fetcher(url, {
    method,
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}
