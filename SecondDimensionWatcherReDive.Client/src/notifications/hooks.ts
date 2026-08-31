import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { NotificationDelivery } from "./types";
import { WebPushSubscriptionSummary } from "./types";

export const useNotificationDeliveries = () =>
  useSWR<NotificationDelivery[]>(
    "/api/notifications/deliveries?take=10",
    fetcher,
    { refreshInterval: 5000 },
  );

export const useWebPushSubscriptions = () =>
  useSWR<WebPushSubscriptionSummary[]>(
    "/api/notifications/web-push/subscriptions",
    fetcher,
    { refreshInterval: 15_000 },
  );
