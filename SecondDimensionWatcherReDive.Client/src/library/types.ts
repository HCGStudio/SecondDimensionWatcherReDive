export type LibraryDownloadState =
  "Any" | "NotDownloaded" | "Downloading" | "Downloaded";
export type LibraryWatchState = "Any" | "Unwatched" | "InProgress" | "Watched";
export type LibrarySourceKind = "Any" | "Torrent" | "MediaLibraryImport";
export type LibrarySearchSort =
  | "PublishedDescending"
  | "TitleAscending"
  | "EpisodeAscending"
  | "ScoreDescending";

export interface LibrarySearchItem {
  animationInfoId: string;
  title: string;
  animationName: string | null;
  animationOriginalName: string | null;
  tmdbId: string | null;
  season: number | null;
  episode: number | null;
  subtitleGroup: string | null;
  resolution: string | null;
  codec: string | null;
  languages: string[];
  isDownloadTracked: boolean;
  isDownloadFinished: boolean;
  isMediaLibraryImport: boolean;
  isWatched: boolean;
  playbackPositionSeconds: number | null;
  virtualPaths: string[];
  releaseScore: number;
  scoreReasons: string[];
  publishedAt: string;
}

export interface LibrarySearchResult {
  items: LibrarySearchItem[];
  nextCursor: string | null;
}

export interface ReleaseUpgradeCandidate {
  currentReleaseId: string;
  candidateReleaseId: string;
  animationName: string;
  season: number;
  episode: number;
  currentScore: number;
  candidateScore: number;
  scoreReasons: string[];
  automatic: boolean;
}

export interface LibraryIntegritySummary {
  tmdbId: string;
  animationName: string;
  season: number;
  expectedEpisodeCount: number | null;
  missingEpisodes: number[];
  duplicateEpisodes: Array<{ episode: number; releaseIds: string[] }>;
  unidentifiedReleaseCount: number;
  upgradeCandidates: ReleaseUpgradeCandidate[];
}

export interface ReleaseUpgradeExecutionResult {
  isSuccess: boolean;
  outcome: string;
  dryRun: boolean;
  requiresDownload: boolean;
  validationErrors: string[];
}
