import { ApiError, apiErrorFromResponse } from "../errors/apiError";
import { IAuthResult } from "./IAuthResult";
import { refreshJwtToken } from "./utils";

let authResult: IAuthResult | null = null;
let refreshPromise: Promise<IAuthResult> | null = null;

function clearAuth() {
  authResult = null;
  localStorage.removeItem("auth");
}

function isAuthResult(value: unknown): value is IAuthResult {
  if (!value || typeof value !== "object") return false;
  const candidate = value as Partial<IAuthResult>;
  return (
    candidate.success === true &&
    typeof candidate.token === "string" &&
    candidate.token.length > 0 &&
    typeof candidate.refreshToken === "string" &&
    candidate.refreshToken.length > 0
  );
}

export const setAuthResult = (result: IAuthResult) => {
  if (result && result.success) {
    authResult = result;
    localStorage.setItem("auth", JSON.stringify(result));
  }
};

export { clearAuth };

async function parseJsonSafe<T>(res: Response): Promise<T> {
  const text = await res.text();
  return text ? JSON.parse(text) : (undefined as T);
}

function sendAuthenticatedRequest(
  input: RequestInfo,
  init: RequestInit | undefined,
  token: string,
): Promise<Response> {
  const headers = new Headers(init?.headers);
  headers.set("Authorization", `Bearer ${token}`);
  return fetch(input, { ...init, headers });
}

/**
 * Fetch a response with the current bearer token, including the same single
 * refresh-and-retry behavior used by JSON API calls. Binary consumers such as
 * authenticated poster loading can then read the response body directly.
 */
export async function authenticatedFetch(
  input: RequestInfo,
  init?: RequestInit,
): Promise<Response> {
  if (!authResult) {
    const storedAuth = localStorage.getItem("auth");
    if (storedAuth) {
      let parsedAuth: unknown;
      try {
        parsedAuth = JSON.parse(storedAuth);
      } catch {
        clearAuth();
      }

      if (isAuthResult(parsedAuth)) {
        authResult = parsedAuth;
      } else {
        clearAuth();
      }
    }
  }

  if (authResult) {
    const res = await sendAuthenticatedRequest(input, init, authResult.token);

    if (res.status !== 401) {
      return res;
    }

    // Token expired — deduplicate concurrent refresh calls
    if (!refreshPromise) {
      refreshPromise = refreshJwtToken(authResult).finally(() => {
        refreshPromise = null;
      });
    }

    try {
      const newAuth = await refreshPromise;
      setAuthResult(newAuth);
    } catch {
      clearAuth();
      window.location.href = "/login";
      throw new ApiError("unauthorized", 401);
    }

    // Retry with new token
    const retryRes = await sendAuthenticatedRequest(
      input,
      init,
      authResult.token,
    );

    if (retryRes.status === 401) {
      clearAuth();
      window.location.href = "/login";
      throw new ApiError("unauthorized", 401);
    }

    return retryRes;
  }

  // No auth available
  return fetch(input, init);
}

export default async function fetcher<JSON = any>(
  input: RequestInfo,
  init?: RequestInit,
): Promise<JSON> {
  const res = await authenticatedFetch(input, init);
  if (!res.ok) {
    throw await apiErrorFromResponse(res);
  }
  return await parseJsonSafe<JSON>(res);
}
