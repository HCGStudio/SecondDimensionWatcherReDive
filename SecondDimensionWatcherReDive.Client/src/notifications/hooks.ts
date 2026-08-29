import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { NotificationDelivery } from "./types";

export const useNotificationDeliveries = () =>
  useSWR<NotificationDelivery[]>(
    "/api/notifications/deliveries?take=10",
    fetcher,
    { refreshInterval: 5000 },
  );
