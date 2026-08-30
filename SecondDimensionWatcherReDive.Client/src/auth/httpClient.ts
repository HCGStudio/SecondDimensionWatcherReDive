import { IAuthResult } from "./IAuthResult";
import { refreshJwtToken } from "./sessionApi";

const AUTH_STORAGE_KEY = "auth";
const AUTH_CHANNEL_NAME = "sdw-auth";
const AUTH_REFRESH_LOCK = "sdw-auth-refresh";
const AUTH_CHANGED_EVENT = "sdw-auth-changed";

const mutationMethods = new Set(["POST", "PUT", "PATCH", "DELETE"]);

type AuthSyncMessage =
  { type: "updated"; value: IAuthResult } | { type: "cleared" };

export interface AuthChangeDetail {
  auth: IAuthResult | null;
  profileChanged: boolean;
}

export const getAuthIdentityKey = (
  value: IAuthResult | null = getAuthResult(),
): string | null =>
  value?.sessionId && value.profileId
    ? `${value.sessionId}\u0000${value.profileId}`
    : null;

const hasSameIdentity = (
  left: IAuthResult | null,
  right: IAuthResult | null,
): boolean =>
  Boolean(
    left && right && getAuthIdentityKey(left) === getAuthIdentityKey(right),
  );

let identityTransitionInProgress = false;
const identityRequests = new Map<string, Set<AbortController>>();

const abortIdentityRequests = (identityKey: string | null) => {
  if (!identityKey) return;
  const controllers = identityRequests.get(identityKey);
  if (!controllers) return;
  for (const controller of controllers) controller.abort();
  identityRequests.delete(identityKey);
};

const notifyAuthChanged = (
  previous: IAuthResult | null,
  current: IAuthResult | null,
) => {
  if (typeof window === "undefined") return;
  const profileChanged = Boolean(
    current &&
    (identityTransitionInProgress ||
      (previous && !hasSameIdentity(previous, current))),
  );
  if (!current || profileChanged) {
    // This runs synchronously before React/SWR sees the new identity. It closes
    // streams and requests which captured the old profile, and prevents
    // beforeunload/cleanup mutations from being sent with the replacement JWT.
    identityTransitionInProgress = true;
    abortIdentityRequests(getAuthIdentityKey(previous));
  } else if (!previous) {
    // A fresh login can resume mutations. Once a profile transition has
    // started, duplicate storage/BroadcastChannel delivery must not reopen the
    // old page before the synchronization hook reloads it.
    identityTransitionInProgress = false;
  }
  window.dispatchEvent(
    new CustomEvent<AuthChangeDetail>(AUTH_CHANGED_EVENT, {
      detail: {
        auth: current,
        profileChanged,
      },
    }),
  );
};

export const subscribeToAuthChanges = (
  listener: (detail: AuthChangeDetail) => void,
): (() => void) => {
  if (typeof window === "undefined") return () => undefined;
  const handler = (event: Event) =>
    listener((event as CustomEvent<AuthChangeDetail>).detail);
  window.addEventListener(AUTH_CHANGED_EVENT, handler);
  return () => window.removeEventListener(AUTH_CHANGED_EVENT, handler);
};

const storage = (): Storage | null =>
  typeof localStorage === "undefined" ? null : localStorage;

const readStoredAuth = (): IAuthResult | null => {
  const raw = storage()?.getItem(AUTH_STORAGE_KEY);
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw) as IAuthResult;
    return parsed?.success &&
      parsed.token &&
      parsed.refreshToken &&
      parsed.sessionId &&
      parsed.profileId
      ? parsed
      : null;
  } catch {
    storage()?.removeItem(AUTH_STORAGE_KEY);
    return null;
  }
};

let authResult: IAuthResult | null = readStoredAuth();
let refreshPromise: Promise<IAuthResult> | null = null;
let authChannel: BroadcastChannel | null = null;

const receiveAuthMessage = (message: AuthSyncMessage) => {
  const previous = authResult;
  authResult = message.type === "updated" ? message.value : null;
  notifyAuthChanged(previous, authResult);
};

if (typeof window !== "undefined") {
  window.addEventListener("storage", (event) => {
    if (event.key !== AUTH_STORAGE_KEY) return;
    const previous = authResult;
    authResult = readStoredAuth();
    notifyAuthChanged(previous, authResult);
  });

  if (typeof BroadcastChannel !== "undefined") {
    authChannel = new BroadcastChannel(AUTH_CHANNEL_NAME);
    authChannel.addEventListener(
      "message",
      (event: MessageEvent<AuthSyncMessage>) => {
        receiveAuthMessage(event.data);
      },
    );
  }
}

function clearAuth(expectedRefreshToken?: string): boolean {
  const current = readStoredAuth();
  if (
    expectedRefreshToken &&
    current &&
    current.refreshToken !== expectedRefreshToken
  ) {
    authResult = current;
    return false;
  }
  const previous = authResult ?? current;
  authResult = null;
  storage()?.removeItem(AUTH_STORAGE_KEY);
  authChannel?.postMessage({ type: "cleared" } satisfies AuthSyncMessage);
  notifyAuthChanged(previous, null);
  return true;
}

export const setAuthResult = (result: IAuthResult) => {
  if (result && result.success) {
    const previous = authResult ?? readStoredAuth();
    authResult = result;
    storage()?.setItem(AUTH_STORAGE_KEY, JSON.stringify(result));
    authChannel?.postMessage({
      type: "updated",
      value: result,
    } satisfies AuthSyncMessage);
    notifyAuthChanged(previous, result);
  }
};

export const getAuthResult = (): IAuthResult | null =>
  readStoredAuth() ?? authResult;

export { clearAuth };

export const clearAuthForSession = (sessionId?: string): boolean => {
  const current = getAuthResult();
  return current && sessionId && current.sessionId !== sessionId
    ? false
    : clearAuth();
};

export class AuthIdentityChangedError extends Error {
  constructor() {
    super("Authentication identity changed");
    this.name = "AuthIdentityChangedError";
  }
}

export interface AuthBoundRequest {
  auth: IAuthResult;
  identityKey: string;
  signal: AbortSignal;
  isCurrent(): boolean;
  abort(): void;
  dispose(): void;
}

/**
 * Capture the session/profile for a request. Profile changes synchronously
 * abort every bound request, including streaming responses. Mutations are not
 * allowed once an identity transition has started because component teardown
 * must never flush old profile state under the new JWT.
 */
export const beginAuthBoundRequest = (
  mutation = false,
  externalSignal?: AbortSignal | null,
): AuthBoundRequest => {
  if (mutation && identityTransitionInProgress) {
    throw new AuthIdentityChangedError();
  }
  const auth = getAuthResult();
  const identityKey = getAuthIdentityKey(auth);
  if (!auth || !identityKey) throw new Error("Unauthorized");

  const controller = new AbortController();
  const controllers = identityRequests.get(identityKey) ?? new Set();
  controllers.add(controller);
  identityRequests.set(identityKey, controllers);

  const abortFromExternalSignal = () => controller.abort();
  if (externalSignal?.aborted) controller.abort();
  else
    externalSignal?.addEventListener("abort", abortFromExternalSignal, {
      once: true,
    });

  let disposed = false;
  const dispose = () => {
    if (disposed) return;
    disposed = true;
    externalSignal?.removeEventListener("abort", abortFromExternalSignal);
    controllers.delete(controller);
    if (controllers.size === 0) identityRequests.delete(identityKey);
  };

  return {
    auth,
    identityKey,
    signal: controller.signal,
    isCurrent: () =>
      !controller.signal.aborted &&
      !identityTransitionInProgress &&
      getAuthIdentityKey() === identityKey,
    abort: () => controller.abort(),
    dispose,
  };
};

export const canSendProfileMutation = (
  capturedIdentityKey: string | null,
  hasWriteAccess = true,
): boolean =>
  Boolean(
    hasWriteAccess &&
    capturedIdentityKey &&
    !identityTransitionInProgress &&
    getAuthIdentityKey() === capturedIdentityKey,
  );

type LockManagerWithRequest = {
  request<T>(
    name: string,
    options: { mode: "exclusive" },
    callback: () => Promise<T>,
  ): Promise<T>;
};

const withRefreshLock = async <T>(callback: () => Promise<T>): Promise<T> => {
  const locks = (
    typeof navigator !== "undefined"
      ? (navigator as Navigator & { locks?: LockManagerWithRequest }).locks
      : undefined
  ) as LockManagerWithRequest | undefined;
  return locks
    ? locks.request(AUTH_REFRESH_LOCK, { mode: "exclusive" }, callback)
    : callback();
};

/**
 * Rotating refresh tokens are shared through localStorage. The Web Lock makes
 * the read/rotate/write sequence atomic across tabs; the second tab observes
 * and reuses the token produced by the first instead of replaying its old one.
 */
export const refreshAuthSession = async (
  staleAuth: IAuthResult,
): Promise<IAuthResult> =>
  withRefreshLock(async () => {
    const current = readStoredAuth();
    if (current && current.refreshToken !== staleAuth.refreshToken) {
      if (!hasSameIdentity(current, staleAuth)) {
        throw new AuthIdentityChangedError();
      }
      authResult = current;
      return current;
    }

    try {
      const refreshInput = current ?? staleAuth;
      const refreshed = await refreshJwtToken(refreshInput);
      if (!refreshed.success || !refreshed.token || !refreshed.refreshToken) {
        throw new Error("Unauthorized");
      }
      if (!hasSameIdentity(refreshed, refreshInput)) {
        throw new AuthIdentityChangedError();
      }
      const beforeCommit = readStoredAuth();
      if (!beforeCommit || !hasSameIdentity(beforeCommit, refreshInput)) {
        throw new AuthIdentityChangedError();
      }
      if (beforeCommit.refreshToken !== refreshInput.refreshToken) {
        // A lockless/concurrent same-identity refresh already won. Preserve its
        // newer rotation rather than rolling shared storage backwards.
        authResult = beforeCommit;
        return beforeCommit;
      }
      setAuthResult(refreshed);
      return refreshed;
    } catch (error) {
      // A browser without Web Locks can still receive a concurrent tab's
      // BroadcastChannel/storage update before its failed request completes.
      const latest = readStoredAuth();
      if (latest && latest.refreshToken !== staleAuth.refreshToken) {
        if (!hasSameIdentity(latest, staleAuth)) {
          throw new AuthIdentityChangedError();
        }
        authResult = latest;
        return latest;
      }
      clearAuth(staleAuth.refreshToken);
      throw error;
    }
  });

const isSuccessfulAuth = (value: IAuthResult): boolean =>
  value.success && Boolean(value.token) && Boolean(value.refreshToken);

/** Serialize endpoints which themselves rotate the current refresh token. */
export const rotateAuthenticatedSession = async (
  path: string,
  body: (auth: IAuthResult) => unknown,
): Promise<IAuthResult> =>
  withRefreshLock(async () => {
    let current = getAuthResult();
    if (!current) throw new Error("Unauthorized");
    const operationIdentity = getAuthIdentityKey(current);

    const send = (auth: IAuthResult) =>
      fetch(path, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${auth.token}`,
        },
        body: JSON.stringify(body(auth)),
      });

    let response = await send(current);
    const afterResponse = getAuthResult();
    if (
      getAuthIdentityKey(afterResponse) !== operationIdentity ||
      afterResponse?.refreshToken !== current.refreshToken
    ) {
      throw new AuthIdentityChangedError();
    }
    if (response.status === 401) {
      const latest = readStoredAuth();
      if (latest && latest.refreshToken !== current.refreshToken) {
        if (!hasSameIdentity(latest, current)) {
          throw new AuthIdentityChangedError();
        }
        current = latest;
      } else {
        const refreshInput = current;
        const refreshed = await refreshJwtToken(refreshInput);
        if (!isSuccessfulAuth(refreshed)) throw new Error("Unauthorized");
        if (getAuthIdentityKey(refreshed) !== operationIdentity) {
          throw new AuthIdentityChangedError();
        }
        const shared = readStoredAuth();
        if (!shared || !hasSameIdentity(shared, refreshInput)) {
          throw new AuthIdentityChangedError();
        }
        if (shared.refreshToken !== refreshInput.refreshToken) {
          current = shared;
        } else {
          current = refreshed;
          setAuthResult(current);
        }
      }
      response = await send(current);
      if (getAuthIdentityKey() !== operationIdentity) {
        throw new AuthIdentityChangedError();
      }
    }

    if (!response.ok) throw new Error(`${response.status}`);
    const rotated = (await response.json()) as IAuthResult;
    if (!isSuccessfulAuth(rotated)) throw new Error("Unauthorized");
    const commitAuth = getAuthResult();
    if (
      getAuthIdentityKey(commitAuth) !== operationIdentity ||
      commitAuth?.refreshToken !== current.refreshToken
    ) {
      throw new AuthIdentityChangedError();
    }
    setAuthResult(rotated);
    return rotated;
  });

async function parseJsonSafe<T>(res: Response): Promise<T> {
  const text = await res.text();
  return text ? (JSON.parse(text) as T) : (undefined as T);
}

export default async function fetcher<JSON = any>(
  input: RequestInfo,
  init?: RequestInit,
): Promise<JSON> {
  const currentAuth = getAuthResult();
  if (currentAuth) {
    const method = (init?.method ?? "GET").toUpperCase();
    const bound = beginAuthBoundRequest(
      mutationMethods.has(method),
      init?.signal,
    );
    authResult = currentAuth;
    const send = (auth: IAuthResult) =>
      fetch(input, {
        ...init,
        signal: bound.signal,
        headers: {
          ...init?.headers,
          Authorization: `Bearer ${auth.token}`,
        },
      });

    try {
      let authForRequest = bound.auth;
      let res = await send(authForRequest);
      if (!bound.isCurrent()) throw new AuthIdentityChangedError();

      if (res.status === 401) {
        // Another request/tab may already have refreshed this same identity.
        // Reuse that token, but never replay a request across session/profile.
        const latest = getAuthResult();
        if (!hasSameIdentity(latest, bound.auth)) {
          throw new AuthIdentityChangedError();
        }
        if (latest!.token !== authForRequest.token) {
          authForRequest = latest!;
        } else {
          if (!refreshPromise) {
            refreshPromise = refreshAuthSession(bound.auth).finally(() => {
              refreshPromise = null;
            });
          }
          authForRequest = await refreshPromise;
          if (
            !bound.isCurrent() ||
            !hasSameIdentity(authForRequest, bound.auth)
          ) {
            throw new AuthIdentityChangedError();
          }
        }

        // Re-check after refresh and again after the retry response. A remote
        // profile switch during either await cancels instead of replaying.
        if (!bound.isCurrent()) throw new AuthIdentityChangedError();
        res = await send(authForRequest);
        if (!bound.isCurrent()) throw new AuthIdentityChangedError();
        if (res.status === 401) {
          clearAuth(authForRequest.refreshToken);
          throw new Error("Unauthorized");
        }
      }

      if (!res.ok) throw new Error(`${res.status}`);
      const result = await parseJsonSafe<JSON>(res);
      if (!bound.isCurrent()) throw new AuthIdentityChangedError();
      return result;
    } catch (error) {
      if (
        identityTransitionInProgress ||
        getAuthIdentityKey() !== bound.identityKey
      ) {
        throw new AuthIdentityChangedError();
      }
      throw error;
    } finally {
      bound.dispose();
    }
  }

  // No auth available
  const res = await fetch(input, init);
  if (!res.ok) {
    throw new Error(`${res.status}`);
  }
  return parseJsonSafe<JSON>(res);
}
