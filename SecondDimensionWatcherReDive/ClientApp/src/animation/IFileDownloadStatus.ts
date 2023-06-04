import { FileDownloadState } from "./FileDownloadState";

export interface IFileDownloadStatus {
  itemId: string;
  progress: number;
  remaining: number;
  speed: number;
  state: FileDownloadState;
}
