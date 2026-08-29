export interface NotificationDelivery {
  id: string;
  type: string;
  status: "Pending" | "Processing" | "Delivered" | "Failed";
  attemptCount: number;
  occurredAt: string;
  lastAttemptAt: string | null;
  deliveredAt: string | null;
  lastError: string | null;
}
