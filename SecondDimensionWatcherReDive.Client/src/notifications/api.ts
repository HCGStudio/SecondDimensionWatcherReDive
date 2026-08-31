import fetcher from "../auth/httpClient";
import { WebPushConfiguration, WebPushSubscriptionSummary } from "./types";

export const sendTestNotification = () =>
  fetcher<{ eventId: string }>("/api/notifications/test", { method: "POST" });

export const getWebPushConfiguration = () =>
  fetcher<WebPushConfiguration>("/api/notifications/web-push/config");

export const registerWebPushSubscription = (subscription: PushSubscription) => {
  const serialized = subscription.toJSON();
  if (!serialized.endpoint || !serialized.keys?.p256dh || !serialized.keys.auth)
    throw new Error("The browser returned an incomplete PushSubscription");
  return fetcher<WebPushSubscriptionSummary>(
    "/api/notifications/web-push/subscriptions",
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        endpoint: serialized.endpoint,
        keys: {
          p256dh: serialized.keys.p256dh,
          auth: serialized.keys.auth,
        },
      }),
    },
  );
};

export const removeCurrentWebPushSubscription = (endpoint: string) =>
  fetcher<void>("/api/notifications/web-push/subscriptions/remove-current", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ endpoint }),
  });

export const removeWebPushSubscription = (id: string) =>
  fetcher<void>(`/api/notifications/web-push/subscriptions/${id}`, {
    method: "DELETE",
  });
