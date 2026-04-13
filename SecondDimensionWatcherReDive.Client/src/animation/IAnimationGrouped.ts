import { IAnimationInfo } from "./IAnimationInfo";

export interface IAnimationWithEpisodes {
  tmdbId: string;
  name: string;
  originalName: string;
  posterPath: string | null;
  episodeCount: number;
  episodes: IAnimationInfo[];
}

export interface IAnimationGroupedResponse {
  animations: IAnimationWithEpisodes[];
  uncategorized: IAnimationInfo[];
}
