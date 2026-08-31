import { IAuthResult } from "./IAuthResult";
import { refreshJwtToken } from "./utils";

const AUTH_STORAGE_KEY = "auth";
let authResult: IAuthResult | null = null;
let refreshPromise: Promise<IAuthResult> | null = null;
let authEpoch = 0;

function readStoredAuth(value: string | null): IAuthResult | null {
  if (!value) return null;
  try {
    const parsed = JSON.parse(value) as Partial<IAuthResult>;
    return parsed.success === true &&
      typeof parsed.token === "string" &&
      typeof parsed.refreshToken === "string"
      ? (parsed as IAuthResult)
      : null;
  } catch {
    return null;
  }
}

function storeAuth(result: IAuthResult) {
  authResult = result;
  try {
    localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(result));
  } catch {
    // Keep the in-memory session usable when storage is unavailable.
  }
}

function clearAuth() {
  authEpoch++;
  authResult = null;
  refreshPromise = null;
  try {
    localStorage.removeItem(AUTH_STORAGE_KEY);
  } catch {
    // Memory is already invalidated; storage failure must not block logout.
  }
}

export const setAuthResult = (result: IAuthResult) => {
  if (result && result.success) {
    authEpoch++;
    refreshPromise = null;
    storeAuth(result);
  }
};

export { clearAuth };

export const getAuthResult = (): IAuthResult | null => {
  if (authResult) return authResult;
  try {
    return readStoredAuth(localStorage.getItem(AUTH_STORAGE_KEY));
  } catch {
    return null;
  }
};

if (typeof window !== "undefined") {
  window.addEventListener("storage", (event) => {
    if (event.key !== AUTH_STORAGE_KEY) return;

    authEpoch++;
    refreshPromise = null;
    authResult = readStoredAuth(event.newValue);
    if (!authResult && window.location.pathname !== "/login") {
      window.location.assign("/login");
    }
  });
}

function beginRefresh(session: IAuthResult): Promise<IAuthResult> {
  const epoch = authEpoch;
  let pending: Promise<IAuthResult>;
  pending = refreshJwtToken(session)
    .then((newAuth) => {
      if (authEpoch !== epoch || authResult !== session)
        throw new Error("Auth session changed during refresh");
      storeAuth(newAuth);
      return newAuth;
    })
    .finally(() => {
      if (refreshPromise === pending) refreshPromise = null;
    });
  refreshPromise = pending;
  return pending;
}

function redirectToLogin() {
  if (typeof window !== "undefined") window.location.href = "/login";
}

async function parseJsonSafe<T>(res: Response): Promise<T> {
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export default async function fetcher<JSON = any>(
  input: RequestInfo,
  init?: RequestInit,
): Promise<JSON> {
  if (authResult) {
    const requestSession = authResult;
    const res = await fetch(input, {
      ...init,
      headers: {
        ...init?.headers,
        Authorization: `Bearer ${requestSession.token}`,
      },
    });

    if (res.status !== 401) {
      if (!res.ok) {
        throw new Error(`${res.status}`);
      }
      return await parseJsonSafe<JSON>(res);
    }

    // Token expired — deduplicate concurrent refresh calls
    if (!refreshPromise) {
      beginRefresh(requestSession);
    }

    try {
      await refreshPromise;
    } catch {
      if (authResult && authResult !== requestSession)
        return await fetcher<JSON>(input, init);
      if (authResult === requestSession) clearAuth();
      redirectToLogin();
      throw new Error("Unauthorized");
    }

    const retrySession = authResult;
    if (!retrySession) {
      redirectToLogin();
      throw new Error("Unauthorized");
    }

    // Retry with new token
    const retryRes = await fetch(input, {
      ...init,
      headers: {
        ...init?.headers,
        Authorization: `Bearer ${retrySession.token}`,
      },
    });

    if (retryRes.status === 401) {
      if (authResult !== retrySession && authResult)
        return await fetcher<JSON>(input, init);
      clearAuth();
      redirectToLogin();
      throw new Error("Unauthorized");
    }

    if (!retryRes.ok) {
      throw new Error(`${retryRes.status}`);
    }

    return await parseJsonSafe<JSON>(retryRes);
  }

  let storedValue: string | null = null;
  try {
    storedValue = localStorage.getItem(AUTH_STORAGE_KEY);
  } catch {
    // Treat unavailable storage as an absent persisted session.
  }
  const storedAuth = readStoredAuth(storedValue);
  if (storedAuth) {
    authEpoch++;
    authResult = storedAuth;
    return await fetcher(input, init);
  }

  // No auth available
  const res = await fetch(input, init);
  if (!res.ok) {
    throw new Error(`${res.status}`);
  }
  return parseJsonSafe<JSON>(res);
}
