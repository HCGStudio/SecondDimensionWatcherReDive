import i18next from "i18next";

import { promptDialog } from "../components/ui/dialogService";
import "../i18n/authResources";
import { IAuthState } from "./IAuthResult";
import fetcher, {
  AuthIdentityChangedError,
  beginAuthBoundRequest,
  rotateAuthenticatedSession,
} from "./httpClient";

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
