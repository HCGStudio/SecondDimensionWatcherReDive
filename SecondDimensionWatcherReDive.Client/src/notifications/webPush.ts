import {
  registerWebPushSubscription,
  removeCurrentWebPushSubscription,
} from "./api";

export const isWebPushSupported = (): boolean =>
  window.isSecureContext &&
  "serviceWorker" in navigator &&
  "PushManager" in window &&
  "Notification" in window;

const decodeBase64Url = (value: string): Uint8Array<ArrayBuffer> => {
  const normalized = value.replace(/-/g, "+").replace(/_/g, "/");
  const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, "=");
  const binary = window.atob(padded);
  const bytes = new Uint8Array(new ArrayBuffer(binary.length));
  for (let index = 0; index < binary.length; index += 1)
    bytes[index] = binary.charCodeAt(index);
  return bytes;
};

const keysEqual = (
  current: ArrayBuffer | null,
  expected: Uint8Array<ArrayBuffer>,
): boolean => {
  if (!current || current.byteLength !== expected.byteLength) return false;
  const currentBytes = new Uint8Array(current);
  return currentBytes.every((value, index) => value === expected[index]);
};

const getRegistration = () => navigator.serviceWorker.getRegistration("/");

const registerCurrentWorker = () => {
  const workerUrl = new URL("./pushServiceWorker.js", import.meta.url);
  return navigator.serviceWorker.register(workerUrl, {
    scope: "/",
    type: "module",
    updateViaCache: "none",
  });
};

export const refreshWebPushServiceWorker = async (): Promise<void> => {
  if (!isWebPushSupported()) return;
  // Parcel fingerprints the worker URL. Re-register the current build whenever
  // an installation already owns this scope so deployments can replace a
  // worker whose previous fingerprinted asset is no longer on the server.
  if (!(await getRegistration())) return;
  await registerCurrentWorker();
};

export const getCurrentWebPushSubscription = async () => {
  if (!isWebPushSupported()) return null;
  const registration = await getRegistration();
  return registration?.pushManager.getSubscription() ?? null;
};

export const hashWebPushEndpoint = async (endpoint: string) => {
  const digest = await crypto.subtle.digest(
    "SHA-256",
    new TextEncoder().encode(endpoint),
  );
  return Array.from(new Uint8Array(digest), (byte) =>
    byte.toString(16).padStart(2, "0"),
  ).join("");
};

export const enableWebPushForCurrentDevice = async (vapidPublicKey: string) => {
  if (!isWebPushSupported()) throw new Error("unsupported");
  const permission = await Notification.requestPermission();
  if (permission !== "granted") throw new Error("permissionDenied");

  const registration = await registerCurrentWorker();
  const expectedKey = decodeBase64Url(vapidPublicKey);
  let subscription = await registration.pushManager.getSubscription();
  if (
    subscription &&
    !keysEqual(subscription.options.applicationServerKey, expectedKey)
  ) {
    await removeCurrentWebPushSubscription(subscription.endpoint);
    await subscription.unsubscribe();
    subscription = null;
  }
  subscription ??= await registration.pushManager.subscribe({
    userVisibleOnly: true,
    applicationServerKey: expectedKey,
  });
  return registerWebPushSubscription(subscription);
};

export const disableWebPushForCurrentDevice = async (): Promise<boolean> => {
  const subscription = await getCurrentWebPushSubscription();
  if (!subscription) return false;
  await removeCurrentWebPushSubscription(subscription.endpoint);
  return subscription.unsubscribe();
};
