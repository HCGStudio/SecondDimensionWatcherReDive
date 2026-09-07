export type TodoType =
  | "ReleaseMatched"
  | "DownloadPendingConfirmation"
  | "DownloadFailed"
  | "Incident"
  | "MetadataReview"
  | "DiskSpaceLow";

export type TodoPriority = "Normal" | "High" | "Critical";

export interface TodoItem {
  key: string;
  type: TodoType;
  priority: TodoPriority;
  title: string;
  detail: string;
  deepLink: string;
  resourceId: string | null;
  occurredAt: string;
  readAt: string | null;
  snoozedUntil: string | null;
}

export interface TodoList {
  items: TodoItem[];
  totalCount: number;
  unreadCount: number;
}

export type TodoStateAction = "markRead" | "markUnread" | "snooze" | "unsnooze";
