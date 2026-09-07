const localTarget = (value) => {
  try {
    const target = new URL(value || "/todo", self.location.origin);
    if (target.origin !== self.location.origin) return "/todo";
    return `${target.pathname}${target.search}${target.hash}`;
  } catch {
    return "/todo";
  }
};

const notificationIcon = new URL("../favicon.svg", import.meta.url).href;

self.addEventListener("push", (event) => {
  let message = {};
  try {
    message = event.data?.json() ?? {};
  } catch {
    message = {};
  }
  const target = localTarget(message.deepLink);
  event.waitUntil(
    self.registration.showNotification(
      message.title || "SecondDimensionWatcher Re:Dive",
      {
        body: message.body || "A notification needs your attention.",
        data: { target },
        icon: notificationIcon,
        tag: message.eventId || undefined,
      },
    ),
  );
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const target = localTarget(event.notification.data?.target);
  event.waitUntil(
    self.clients
      .matchAll({ type: "window", includeUncontrolled: true })
      .then(async (windows) => {
        const existing = windows.find(
          (client) => new URL(client.url).origin === self.location.origin,
        );
        if (existing) {
          await existing.navigate(target);
          return existing.focus();
        }
        return self.clients.openWindow(target);
      }),
  );
});
