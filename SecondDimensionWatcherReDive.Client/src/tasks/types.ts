export interface ITask {
  id: string;
  interval: string;
  isEnabled: boolean;
  lastRunAt: string | null;
  isRunning: boolean;
}
