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

export default async function fetcher<JSON = any>(
  input: RequestInfo,
  init?: RequestInit,
): Promise<JSON> {
  if (authResult) {
    const res = await fetch(input, {
      ...init,
      headers: {
        ...init?.headers,
        Authorization: `Bearer ${authResult.token}`,
      },
    });

    if (res.status !== 401) {
      if (!res.ok) {
        throw await apiErrorFromResponse(res);
      }
      return await parseJsonSafe<JSON>(res);
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
    const retryRes = await fetch(input, {
      ...init,
      headers: {
        ...init?.headers,
        Authorization: `Bearer ${authResult.token}`,
      },
    });

    if (retryRes.status === 401) {
      clearAuth();
      window.location.href = "/login";
      throw new ApiError("unauthorized", 401);
    }

    if (!retryRes.ok) {
      throw await apiErrorFromResponse(retryRes);
    }

    return await parseJsonSafe<JSON>(retryRes);
  }

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
      // Keep the authenticated request outside the storage parsing catch: API
      // and network failures must propagate unchanged and must never trigger a
      // second anonymous request.
      return fetcher(input, init);
    }

    clearAuth();
  }

  // No auth available
  const res = await fetch(input, init);
  if (!res.ok) {
    throw await apiErrorFromResponse(res);
  }
  return parseJsonSafe<JSON>(res);
}
