import i18next from "i18next";

import { promptDialog } from "../components/ui/dialogService";
import { ApiError } from "../errors/apiError";
import { IAuthResult, IAuthState } from "./IAuthResult";
import fetcher, {
  AuthIdentityChangedError,
  beginAuthBoundRequest,
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

export const reauthenticate = async (
  password: string,
  signal?: AbortSignal,
) => {
  const bound = beginAuthBoundRequest(true, signal);
  try {
    return await rotateAuthenticatedSession(
      "/api/auth/reauthenticate",
      (auth) => {
        if (!bound.isCurrent()) throw new AuthIdentityChangedError();
        return { password, refreshToken: auth.refreshToken };
      },
    );
  } finally {
    bound.dispose();
  }
};

export const reauthenticateInteractively = async (
  promptMessage: string,
  signal?: AbortSignal,
): Promise<boolean> => {
  const bound = beginAuthBoundRequest(true, signal);
  try {
    const status = await fetcher<IAuthState>("/api/auth/verify", {
      signal: bound.signal,
    });
    if (!bound.isCurrent()) throw new AuthIdentityChangedError();
    if (status.hasPassword === false) {
      const passkeys = await import("./passkeys");
      if (!bound.isCurrent()) throw new AuthIdentityChangedError();
      if (!passkeys.isPasskeySupported()) {
        throw new Error(
          i18next.t(
            passkeys.isPasskeyHttps()
              ? "auth:passkeys.unsupported"
              : "auth:passkeys.httpsRequired",
          ),
        );
      }
      try {
        await passkeys.reauthenticateWithPasskey(bound.signal);
      } catch (error) {
        if (passkeys.isPasskeyCancellation(error)) return false;
        throw error;
      }
    } else {
      const password = await promptDialog(promptMessage, {
        inputType: "password",
        autoComplete: "current-password",
      });
      if (!bound.isCurrent()) throw new AuthIdentityChangedError();
      if (!password) return false;
      await reauthenticate(password, bound.signal);
    }
    if (!bound.isCurrent()) throw new AuthIdentityChangedError();
    return true;
  } finally {
    bound.dispose();
  }
};

export const retryAfterReauthentication = async <T>(
  operation: () => Promise<T>,
  promptMessage: string,
  signal?: AbortSignal,
): Promise<T> => {
  const bound = beginAuthBoundRequest(true, signal);
  try {
    try {
      return await operation();
    } catch (error) {
      if (
        !(error instanceof Error) ||
        error.message !== "403" ||
        (error instanceof ApiError && error.code === "httpsRequired")
      )
        throw error;
      if (!bound.isCurrent()) throw new AuthIdentityChangedError();
      if (!(await reauthenticateInteractively(promptMessage, bound.signal)))
        throw error;
      if (!bound.isCurrent()) throw new AuthIdentityChangedError();
      return await operation();
    }
  } finally {
    bound.dispose();
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
