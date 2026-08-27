import { IAuthResult } from "./IAuthResult";
import { refreshJwtToken } from "./utils";

let authResult: IAuthResult | null = null;
let refreshPromise: Promise<IAuthResult> | null = null;

function clearAuth() {
  authResult = null;
  localStorage.removeItem("auth");
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
  return text ? JSON.parse(text) : undefined;
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
        throw new Error(`${res.status}`);
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
      throw new Error("Unauthorized");
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
      throw new Error("Unauthorized");
    }

    if (!retryRes.ok) {
      throw new Error(`${retryRes.status}`);
    }

    return await parseJsonSafe<JSON>(retryRes);
  }

  if (localStorage.getItem("auth")) {
    authResult = JSON.parse(localStorage.getItem("auth")!);
    return await fetcher(input, init);
  }

  // No auth available
  const res = await fetch(input, init);
  if (!res.ok) {
    throw new Error(`${res.status}`);
  }
  return parseJsonSafe<JSON>(res);
}
