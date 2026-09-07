import { SecretDraft, SecretState } from "./systemTypes";

export const isIntegerInRange = (
  value: number,
  minimum: number,
  maximum: number,
): boolean =>
  Number.isFinite(value) &&
  Number.isInteger(value) &&
  value >= minimum &&
  value <= maximum;

export const isHttpEndpoint = (value: string): boolean => {
  try {
    const url = new URL(value);
    return (
      (url.protocol === "http:" || url.protocol === "https:") &&
      !!url.hostname &&
      !url.username &&
      !url.password &&
      !url.search &&
      !url.hash
    );
  } catch {
    return false;
  }
};

export const isCodexEndpoint = (value: string): boolean => {
  try {
    const url = new URL(value);
    if (!url.hostname || url.username || url.password || url.search || url.hash)
      return false;
    if (url.protocol === "wss:") return true;
    if (url.protocol !== "ws:") return false;
    const hostname = url.hostname.replace(/^\[|\]$/g, "").toLowerCase();
    return (
      hostname === "localhost" ||
      hostname === "::1" ||
      /^127(?:\.\d{1,3}){3}$/.test(hostname)
    );
  } catch {
    return false;
  }
};

export const isRemoteWebSocketEndpoint = (value: string): boolean => {
  try {
    const url = new URL(value);
    if (url.protocol !== "wss:") return false;
    const hostname = url.hostname.replace(/^\[|\]$/g, "").toLowerCase();
    return !(
      hostname === "localhost" ||
      hostname === "::1" ||
      /^127(?:\.\d{1,3}){3}$/.test(hostname)
    );
  } catch {
    return false;
  }
};

export const willSecretBeConfigured = (
  state: SecretState,
  draft: SecretDraft,
): boolean => {
  if (draft.value.trim()) return true;
  if (draft.operation === "clear") return false;
  // Reset reveals the deployment fallback, whose presence is intentionally not exposed while a
  // runtime override is active. Let the server make the authoritative configured/not-configured
  // decision instead of blocking a potentially valid reset in the browser.
  if (draft.operation === "reset") return true;
  return state.isConfigured;
};

const originOf = (value: string): string | null => {
  try {
    return new URL(value).origin;
  } catch {
    return null;
  }
};

export const requiresCredentialChange = (
  previousUrl: string,
  nextUrl: string,
  secret: SecretState,
  draft: SecretDraft,
): boolean =>
  secret.isConfigured &&
  originOf(previousUrl) !== originOf(nextUrl) &&
  !draft.value.trim() &&
  draft.operation === "keep";

export const isAbsoluteServerPath = (value: string): boolean => {
  const path = value.trim();
  return (
    path.startsWith("/") ||
    /^[A-Za-z]:[\\/]/.test(path) ||
    /^(?:\\\\|\/\/)[^\\/]+[\\/][^\\/]+/.test(path)
  );
};

export const normalizeServerPathForComparison = (value: string): string => {
  const path = value.trim();
  if (path === "/" || /^[A-Za-z]:[\\/]$/.test(path)) return path;
  return path.replace(/[\\/]+$/, "");
};

export const isIpAddress = (value: string): boolean => {
  const address = value.trim();
  const ipv4Parts = address.split(".");
  if (ipv4Parts.length === 4)
    return ipv4Parts.every(
      (part) => /^\d{1,3}$/.test(part) && Number(part) <= 255,
    );

  const ipv6Address = address.split("%", 1)[0];
  if (!ipv6Address.includes(":")) return false;
  try {
    return new URL(`http://[${ipv6Address}]/`).hostname.length > 0;
  } catch {
    return false;
  }
};

export const isIpCidr = (value: string): boolean => {
  const separator = value.lastIndexOf("/");
  if (separator <= 0) return false;
  const address = value.slice(0, separator);
  const prefix = Number(value.slice(separator + 1));
  if (!isIpAddress(address)) return false;
  if (!address.includes(":")) return isIntegerInRange(prefix, 0, 32);

  const normalized = new URL(`http://[${address.split("%", 1)[0]}]/`).hostname
    .slice(1, -1)
    .toLowerCase();
  return normalized.startsWith("::ffff:")
    ? isIntegerInRange(prefix, 96, 128)
    : isIntegerInRange(prefix, 0, 128);
};
