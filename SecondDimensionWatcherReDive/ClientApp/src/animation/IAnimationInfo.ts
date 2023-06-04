import { IAnimation } from "./IAnimation";
import { IAnimationGroup } from "./IAnimationGroup";

export interface IAnimationInfo {
  id: string;
  title: string;
  description: string;
  publishTime: string;
  isDownloadTracked: boolean;
  isDownloadFinished: boolean;
  group?: IAnimationGroup;
  animation?: IAnimation;
}
