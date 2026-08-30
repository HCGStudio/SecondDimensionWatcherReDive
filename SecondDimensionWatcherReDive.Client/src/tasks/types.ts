export interface ITask {
  id: string;
  interval: string;
  isEnabled: boolean;
  lastRunAt: string | null;
  isRunning: boolean;
}

export interface IDurableJob {
  id: string;
  type: "downloadCompletion";
  status: "deadLetter";
  stage: "mapFiles" | "notify" | "invokePlugins" | "done";
  attemptCount: number;
  createdAt: string;
  updatedAt: string;
  nextAttemptAt: string;
  lastAttemptAt: string | null;
  completedAt: string | null;
  lastError: string | null;
}

export interface IDurableJobPage {
  items: IDurableJob[];
  totalCount: number;
}
