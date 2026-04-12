export interface ITask {
  name: string;
  description: string;
  interval: string;
  isEnabled: boolean;
  lastRunAt: string | null;
  isRunning: boolean;
}
