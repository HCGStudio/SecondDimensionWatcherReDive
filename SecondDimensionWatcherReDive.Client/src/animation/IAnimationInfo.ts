import { IAnimation } from "./IAnimation";
import { IAnimationGroup } from "./IAnimationGroup";

export type SubscriptionAutomationDisposition =
  | "Notified"
  | "PendingConfirmation"
  | "AutoDownloadQueued"
  | "AutoDownloadFailed"
  | "ManualDownloadQueued"
  | "DownloadCompleted"
  | "DownloadCancelled";

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
  sourceFeedId?: string | null;
  releaseSizeBytes?: number | null;
  automationDisposition?: SubscriptionAutomationDisposition | null;
  automationExplanationJson?: string | null;
}
