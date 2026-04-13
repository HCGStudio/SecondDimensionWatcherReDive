import { IAnimation } from "./IAnimation";
import { IAnimationGroup } from "./IAnimationGroup";

export interface IAnimationInfo {
  id: string;
  title: string;
  description: string;
  publishTime: string;
  isDownloadTracked: boolean;
  isDownloadFinished: boolean;
  season?: number | null;
  episode?: number | null;
  group?: IAnimationGroup;
  animation?: IAnimation;
  isAiProcessed: boolean;
}
