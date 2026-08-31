import { ApiError, apiErrorFromResponse } from "../errors/apiError";
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
      parsed.token.length > 0 &&
      typeof parsed.refreshToken === "string" &&
      parsed.refreshToken.length > 0
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

function sendAuthenticatedRequest(
  input: RequestInfo,
  init: RequestInit | undefined,
  token: string,
): Promise<Response> {
  const headers = new Headers(
    input instanceof Request ? input.headers : undefined,
  );
  new Headers(init?.headers).forEach((value, name) => headers.set(name, value));
  headers.set("Authorization", `Bearer ${token}`);
  return fetch(input, { ...init, headers });
}

/**
 * Fetch a response with the current bearer token, including the same single
 * refresh-and-retry and auth-epoch fencing used by JSON API calls. Binary
 * consumers such as authenticated poster loading can read the body directly.
 */
export async function authenticatedFetch(
  input: RequestInfo,
  init?: RequestInit,
): Promise<Response> {
  if (authResult) {
    const requestSession = authResult;
    const response = await sendAuthenticatedRequest(
      input,
      init,
      requestSession.token,
    );

    if (response.status !== 401) return response;

    if (!refreshPromise) beginRefresh(requestSession);

    try {
      await refreshPromise;
    } catch {
      if (authResult && authResult !== requestSession)
        return await authenticatedFetch(input, init);
      if (authResult === requestSession) clearAuth();
      redirectToLogin();
      throw new ApiError("unauthorized", 401);
    }

    const retrySession = authResult;
    if (!retrySession) {
      redirectToLogin();
      throw new ApiError("unauthorized", 401);
    }

    const retryResponse = await sendAuthenticatedRequest(
      input,
      init,
      retrySession.token,
    );
    if (retryResponse.status === 401) {
      if (authResult !== retrySession && authResult)
        return await authenticatedFetch(input, init);
      clearAuth();
      redirectToLogin();
      throw new ApiError("unauthorized", 401);
    }

    return retryResponse;
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
    return await authenticatedFetch(input, init);
  }

  return fetch(input, init);
}

export default async function fetcher<JSON = any>(
  input: RequestInfo,
  init?: RequestInit,
): Promise<JSON> {
  const response = await authenticatedFetch(input, init);
  if (!response.ok) throw await apiErrorFromResponse(response);
  return await parseJsonSafe<JSON>(response);
}
