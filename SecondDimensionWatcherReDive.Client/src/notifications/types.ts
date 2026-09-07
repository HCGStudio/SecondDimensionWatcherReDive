export interface NotificationDelivery {
  id: string;
  eventId: string;
  channel: "Webhook" | "WebPush";
  type: string;
  status: "Pending" | "Processing" | "Delivered" | "Failed";
  attemptCount: number;
  occurredAt: string;
  lastAttemptAt: string | null;
  deliveredAt: string | null;
  lastError: string | null;
}

export interface WebPushConfiguration {
  enabled: boolean;
  vapidPublicKey: string;
}

export interface WebPushSubscriptionSummary {
  id: string;
  endpointOrigin: string;
  endpointHash: string;
  createdAt: string;
  updatedAt: string;
  lastSuccessAt: string | null;
  lastFailureAt: string | null;
  lastError: string | null;
}
