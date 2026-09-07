import { IAuthResult } from "./IAuthResult";
import fetcher, {
  clearAuthForSession,
  getAuthResult,
  rotateAuthenticatedSession,
} from "./httpClient";

export { refreshJwtToken } from "./sessionApi";

interface LoginOptions {
  username?: string;
  deviceName?: string;
  profileName?: string;
}

export const login = async (
  password: string,
  options: LoginOptions = {},
): Promise<IAuthResult> => {
  const response = await fetch("/api/auth/login", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ password, ...options }),
  });
  if (!response.ok) throw new Error(`${response.status}`);
  return (await response.json()) as IAuthResult;
};

export const register = async (
  password: string,
  options: LoginOptions = {},
): Promise<IAuthResult | null> => {
  const response = await fetch("/api/auth/register", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ password, ...options }),
  });
  if (!response.ok) throw new Error(`${response.status}`);
  return (await response.json()) as IAuthResult;
};

export const switchProfile = (profileId: string, pin?: string) =>
  rotateAuthenticatedSession("/api/accounts/profiles/switch", (auth) => ({
    profileId,
    pin: pin || null,
    refreshToken: auth.refreshToken,
  }));

export const reauthenticate = (password: string) =>
  rotateAuthenticatedSession("/api/auth/reauthenticate", (auth) => ({
    password,
    refreshToken: auth.refreshToken,
  }));

export const retryAfterReauthentication = async <T>(
  operation: () => Promise<T>,
  promptMessage: string,
): Promise<T> => {
  try {
    return await operation();
  } catch (error) {
    if (!(error instanceof Error) || error.message !== "403") throw error;
    const password = window.prompt(promptMessage);
    if (!password) throw error;
    await reauthenticate(password);
    return operation();
  }
};

export const logout = async (): Promise<void> => {
  const sessionId = getAuthResult()?.sessionId;
  try {
    await fetcher("/api/auth/logout", { method: "POST" });
  } catch {
    // Local logout must remain available if the session is already invalid or
    // the server is unreachable. The server revocation above is best-effort.
  } finally {
    // A late logout from an old tab must not erase a newer login session from
    // shared storage. Profile changes within this same session are still
    // cleared because the server revocation applies to the whole session.
    clearAuthForSession(sessionId);
  }
};
