import fetcher from "../auth/httpClient";

export const sendTestNotification = () =>
  fetcher<{ eventId: string }>("/api/notifications/test", { method: "POST" });
