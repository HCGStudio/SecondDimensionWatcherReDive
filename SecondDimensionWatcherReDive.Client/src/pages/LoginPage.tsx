import React from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { mutate } from "swr";

import { useAllowRegister, useLoginStatus } from "../auth/hooks";
import {
  getAuthIdentityKey,
  getAuthResult,
  setAuthResult,
  subscribeToAuthChanges,
} from "../auth/httpClient";
import {
  isPasskeyCancellation,
  isPasskeyHttps,
  isPasskeySupported,
  loginWithPasskey,
} from "../auth/passkeys";
import { login, register } from "../auth/utils";
import { BrandIcon } from "../components/BrandIcon";
import { Button } from "../components/ui/Button";
import { FormRow } from "../components/ui/FormRow";
import { Input } from "../components/ui/Input";
import { PasswordInput } from "../components/ui/PasswordInput";
import { ApiError } from "../errors/apiError";
import "../i18n/authResources";
import { PageTemplate } from "./PageTemplate";

export const LoginPage: React.FC = () => {
  const { t } = useTranslation("auth");
  const { data: registerInfo } = useAllowRegister();
  const { data: status, error: statusError } = useLoginStatus();
  const isAuthenticated = Boolean(status && !statusError && getAuthResult());
  const [password, setPassword] = React.useState("");
  const [username, setUsername] = React.useState("admin");
  const [profileName, setProfileName] = React.useState("Home");
  const [passwordConfirm, setPasswordConfirm] = React.useState("");
  const [loginFailed, setLoginFailed] = React.useState(false);
  const [registerFailed, setRegisterFailed] = React.useState(false);
  const [isSubmitting, setIsSubmitting] = React.useState(false);
  const navigate = useNavigate();
  const [passkeyError, setPasskeyError] = React.useState<string | null>(null);
  const passkeyRequest = React.useRef<AbortController | null>(null);
  React.useEffect(() => {
    const unsubscribe = subscribeToAuthChanges(() =>
      passkeyRequest.current?.abort(),
    );
    return () => {
      unsubscribe();
      passkeyRequest.current?.abort();
    };
  }, []);

  const onPasskeyLogin = async () => {
    if (isSubmitting) return;
    const controller = new AbortController();
    passkeyRequest.current = controller;
    const identity = getAuthIdentityKey();
    setIsSubmitting(true);
    setPasskeyError(null);
    setLoginFailed(false);
    try {
      const result = await loginWithPasskey(username.trim(), controller.signal);
      if (controller.signal.aborted || getAuthIdentityKey() !== identity)
        return;
      if (!result.success) throw new Error("Passkey sign-in failed");
      setAuthResult(result);
      await mutate("/api/auth/verify");
    } catch (error) {
      if (!controller.signal.aborted) {
        setPasskeyError(
          t(
            isPasskeyCancellation(error)
              ? "passkeys.canceled"
              : error instanceof ApiError &&
                  error.code === "passkeys_mock_unavailable"
                ? "passkeys.mockUnavailable"
                : error instanceof ApiError && error.status === 429
                  ? "passkeys.rateLimited"
                  : "passkeys.failed",
          ),
        );
      }
    } finally {
      if (passkeyRequest.current === controller) {
        passkeyRequest.current = null;
        setIsSubmitting(false);
      }
    }
  };

  const onPasswordChange: React.ChangeEventHandler<HTMLInputElement> = (ev) => {
    setPassword(ev.target.value);
    setLoginFailed(false);
  };
  const onPasswordConfirmChange: React.ChangeEventHandler<HTMLInputElement> = (
    ev,
  ) => {
    setPasswordConfirm(ev.target.value);
  };

  const onRegister = React.useCallback(
    async (e?: React.FormEvent) => {
      e?.preventDefault();
      if (password !== passwordConfirm || isSubmitting) return;
      setIsSubmitting(true);
      setRegisterFailed(false);
      try {
        const r = await register(password, {
          username,
          profileName,
          deviceName: navigator.userAgent,
        });
        if (r?.success) {
          setAuthResult(r);
          await mutate("/api/auth/verify");
        } else {
          setRegisterFailed(true);
        }
      } catch {
        setRegisterFailed(true);
      } finally {
        setIsSubmitting(false);
      }
    },
    [password, passwordConfirm, isSubmitting, profileName, username],
  );

  const onLogin = React.useCallback(
    async (e?: React.FormEvent) => {
      e?.preventDefault();
      if (isSubmitting) return;
      setIsSubmitting(true);
      setLoginFailed(false);
      try {
        const r = await login(password, {
          username,
          deviceName: navigator.userAgent,
        });
        if (r?.success) {
          setAuthResult(r);
          await mutate("/api/auth/verify");
        } else {
          setLoginFailed(true);
        }
      } catch {
        setLoginFailed(true);
      } finally {
        setIsSubmitting(false);
      }
    },
    [password, username, isSubmitting],
  );

  React.useEffect(() => {
    if (isAuthenticated) navigate("/", { replace: true });
  }, [navigate, isAuthenticated]);

  return (
    <PageTemplate>
      <div className="mx-auto my-5 max-w-md rounded-xl border border-border bg-surface p-6 sm:my-10 sm:p-8">
        <BrandIcon className="mb-6 h-12 w-12" />
        {isAuthenticated ? null : registerInfo?.allow ? (
          <form onSubmit={onRegister}>
            <h2 className="font-sans text-2xl font-medium leading-heading">
              {t("setupTitle")}
            </h2>
            <p className="mt-2 text-sm text-muted leading-body">
              {t("setupHelp")}
            </p>
            <div className="mt-6 space-y-4">
              <FormRow label={t("username")}>
                <Input
                  autoComplete="username"
                  disabled={isSubmitting}
                  value={username}
                  onChange={(event) => setUsername(event.target.value)}
                />
              </FormRow>
              <FormRow label={t("profileName")}>
                <Input
                  value={profileName}
                  onChange={(event) => setProfileName(event.target.value)}
                />
              </FormRow>
              <FormRow label={t("password")}>
                <PasswordInput
                  placeholder={t("passwordPlaceholder")}
                  disabled={isSubmitting}
                  value={password}
                  onChange={onPasswordChange}
                />
              </FormRow>
              <FormRow
                label={t("repeatPassword")}
                isInvalid={
                  (password !== passwordConfirm &&
                    passwordConfirm.length > 0) ||
                  registerFailed
                }
                error={[registerFailed ? t("registerFailed") : t("mismatch")]}
              >
                <PasswordInput
                  placeholder={t("repeatPassword")}
                  value={passwordConfirm}
                  onChange={onPasswordConfirmChange}
                  isInvalid={
                    password !== passwordConfirm && passwordConfirm.length > 0
                  }
                />
              </FormRow>
              <Button
                type="submit"
                className="w-full"
                disabled={
                  isSubmitting ||
                  password !== passwordConfirm ||
                  password.length === 0
                }
              >
                {isSubmitting ? t("registering") : t("register")}
              </Button>
            </div>
          </form>
        ) : (
          <form onSubmit={onLogin}>
            <h2 className="font-sans text-2xl font-medium leading-heading">
              {t("welcomeBack")}
            </h2>
            <div className="mt-6 space-y-4">
              <FormRow label={t("username")}>
                <Input
                  autoComplete="username"
                  disabled={isSubmitting}
                  value={username}
                  onChange={(event) => setUsername(event.target.value)}
                />
              </FormRow>
              <FormRow
                label={t("password")}
                isInvalid={loginFailed}
                error={[t("wrongPassword")]}
              >
                <PasswordInput
                  placeholder={t("passwordPlaceholder")}
                  disabled={isSubmitting}
                  value={password}
                  onChange={onPasswordChange}
                  isInvalid={loginFailed}
                />
              </FormRow>
              <Button
                type="submit"
                className="w-full"
                disabled={isSubmitting || password.length === 0}
              >
                {isSubmitting ? t("loggingIn") : t("login")}
              </Button>
              <Button
                type="button"
                variant="outline"
                className="w-full"
                disabled={
                  isSubmitting || !username.trim() || !isPasskeySupported()
                }
                onClick={() => void onPasskeyLogin()}
              >
                {t("passkeys.login")}
              </Button>
              {!isPasskeySupported() ? (
                <p className="text-xs leading-body text-muted">
                  {t(
                    isPasskeyHttps()
                      ? "passkeys.unsupported"
                      : "passkeys.httpsRequired",
                  )}
                </p>
              ) : null}
              {passkeyError ? (
                <p role="alert" className="text-sm text-error">
                  {passkeyError}
                </p>
              ) : null}
            </div>
          </form>
        )}
      </div>
    </PageTemplate>
  );
};
