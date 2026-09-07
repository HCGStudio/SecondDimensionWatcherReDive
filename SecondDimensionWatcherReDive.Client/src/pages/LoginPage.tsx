import React from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { mutate } from "swr";

import { useAllowRegister, useLoginStatus } from "../auth/hooks";
import { setAuthResult } from "../auth/httpClient";
import { login, register } from "../auth/utils";
import { Button } from "../components/ui/Button";
import { FormRow } from "../components/ui/FormRow";
import { PasswordInput } from "../components/ui/PasswordInput";
import { PageTemplate } from "./PageTemplate";

export const LoginPage: React.FC = () => {
  const { t } = useTranslation("auth");
  const { data: registerInfo } = useAllowRegister();
  const { data: status } = useLoginStatus();
  const [password, setPassword] = React.useState("");
  const [passwordConfirm, setPasswordConfirm] = React.useState("");
  const [loginFailed, setLoginFailed] = React.useState(false);
  const [registerFailed, setRegisterFailed] = React.useState(false);
  const [isSubmitting, setIsSubmitting] = React.useState(false);
  const navigate = useNavigate();

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
        const r = await register(password);
        if (r?.success) {
          setAuthResult(r);
          await mutate("/api/auth/verify", true, { revalidate: false });
          navigate("/");
        } else {
          setRegisterFailed(true);
        }
      } catch {
        setRegisterFailed(true);
      } finally {
        setIsSubmitting(false);
      }
    },
    [password, passwordConfirm, isSubmitting, navigate],
  );

  const onLogin = React.useCallback(
    async (e?: React.FormEvent) => {
      e?.preventDefault();
      if (isSubmitting) return;
      setIsSubmitting(true);
      setLoginFailed(false);
      try {
        const r = await login(password);
        if (r?.success) {
          setAuthResult(r);
          await mutate("/api/auth/verify", true, { revalidate: false });
          navigate("/");
        } else {
          setLoginFailed(true);
        }
      } catch {
        setLoginFailed(true);
      } finally {
        setIsSubmitting(false);
      }
    },
    [password, isSubmitting, navigate],
  );

  React.useEffect(() => {
    if (status) navigate("/");
  }, [navigate, status]);

  return (
    <PageTemplate>
      <div className="mx-auto max-w-md">
        {status ? null : registerInfo?.allow ? (
          <form onSubmit={onRegister}>
            <h2 className="font-serif text-2xl font-medium leading-heading">
              {t("setupTitle")}
            </h2>
            <p className="mt-2 text-sm text-muted leading-body">
              {t("setupHelp")}
            </p>
            <div className="mt-6 space-y-4">
              <FormRow label={t("password")}>
                <PasswordInput
                  placeholder={t("passwordPlaceholder")}
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
            <h2 className="font-serif text-2xl font-medium leading-heading">
              {t("welcomeBack")}
            </h2>
            <div className="mt-6 space-y-4">
              <FormRow
                label={t("password")}
                isInvalid={loginFailed}
                error={[t("wrongPassword")]}
              >
                <PasswordInput
                  placeholder={t("passwordPlaceholder")}
                  value={password}
                  onChange={onPasswordChange}
                  isInvalid={loginFailed}
                />
              </FormRow>
              <Button
                type="submit"
                disabled={isSubmitting || password.length === 0}
              >
                {isSubmitting ? t("loggingIn") : t("login")}
              </Button>
            </div>
          </form>
        )}
      </div>
    </PageTemplate>
  );
};
