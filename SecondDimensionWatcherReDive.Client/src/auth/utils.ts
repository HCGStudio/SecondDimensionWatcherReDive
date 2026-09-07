import { IAuthResult } from "./IAuthResult";

export const login = async (password: string): Promise<IAuthResult> => {
  const response = await fetch("/api/auth/login", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ password }),
  });
  return (await response.json()) as IAuthResult;
};

export const refreshJwtToken = async (
  oldToken: IAuthResult,
): Promise<IAuthResult> => {
  const response = await fetch("/api/auth/refresh", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(oldToken),
  });
  const result = (await response.json()) as IAuthResult;
  if (!response.ok || !result.success) throw new Error("Refresh failed");
  return result;
};

export const revokeSession = async (auth: IAuthResult): Promise<void> => {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 5_000);
  try {
    const response = await fetch("/api/auth/logout", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${auth.token}`,
      },
      body: JSON.stringify({ refreshToken: auth.refreshToken }),
      signal: controller.signal,
    });
    if (!response.ok) {
      throw new Error(`Logout failed (${response.status})`);
    }
  } finally {
    clearTimeout(timeout);
  }
};

export const register = async (
  password: string,
): Promise<IAuthResult | null> => {
  const response = await fetch("/api/auth/register", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ password }),
  });
  return (await response.json()) as IAuthResult;
};
