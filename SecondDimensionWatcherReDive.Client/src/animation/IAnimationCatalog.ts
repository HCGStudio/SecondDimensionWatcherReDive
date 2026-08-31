import { IAnimationInfo } from "./IAnimationInfo";

export interface IAnimationCatalogItem {
  tmdbId: string;
  name: string;
  originalName: string;
  posterPath: string | null;
  episodeCount: number;
  releaseCount: number;
  automationAttentionCount: number;
  latestPublishTime: string;
}

export interface IAnimationCatalogResponse {
  items: IAnimationCatalogItem[];
  nextCursor: string | null;
  revision: number;
}

export interface IAnimationInfoSummaryResponse {
  items: IAnimationInfo[];
  nextCursor: string | null;
  revision: number;
}

export interface IAnimationEpisodeResponse {
  animation: IAnimationCatalogItem;
  episodes: IAnimationInfo[];
  nextCursor: string | null;
  revision: number;
}

export interface IAnimationCatalogRevisionResponse {
  revision: number;
}
