import React from "react";
import { useTranslation } from "react-i18next";
import useSWR, { mutate as mutateAll } from "swr";

import { Fingerprint, KeyRound, Plus, RefreshCw } from "lucide-react";

import {
  AuthIdentityChangedError,
  beginAuthBoundRequest,
} from "../../auth/httpClient";
import {
  PasskeySettings,
  isPasskeyCancellation,
  isPasskeyHttps,
  isPasskeySupported,
  registerPasskey,
  removePasswordWithPasskey,
} from "../../auth/passkeys";
import { retryAfterReauthentication } from "../../auth/utils";
import { ApiError } from "../../errors/apiError";
import { useToast } from "../ToastProvider";
import { Button } from "../ui/Button";
import { Card } from "../ui/Card";
import { FormRow } from "../ui/FormRow";
import { Input } from "../ui/Input";
import { Spinner } from "../ui/Spinner";
import { confirmDialog } from "../ui/dialogService";
import { SettingsSectionHeader } from "./SettingsControls";

export const SecuritySettingsSection: React.FC = () => {
  const { t, i18n } = useTranslation(["settings", "auth"]);
  const { addToast } = useToast();
  const https = isPasskeyHttps();
  const supported = isPasskeySupported();
  const { data, error, mutate } = useSWR<PasskeySettings>(
    https ? "/api/auth/passkeys" : null,
  );
  const [name, setName] = React.useState("");
  const [busy, setBusy] = React.useState(false);
  const [actionError, setActionError] = React.useState<string | null>(null);
  const busyRef = React.useRef(false);
  const lifecycle = React.useRef<AbortController | null>(null);
  React.useEffect(() => {
    const controller = new AbortController();
    lifecycle.current = controller;
    return () => controller.abort();
  }, []);

  const run = async (
    operation: (
      signal: AbortSignal,
      ensureCurrent: () => void,
    ) => Promise<void>,
  ) => {
    if (busyRef.current) return;
    busyRef.current = true;
    setBusy(true);
    setActionError(null);
    let bound: ReturnType<typeof beginAuthBoundRequest> | undefined;
    try {
      bound = beginAuthBoundRequest(true, lifecycle.current?.signal);
      const request = bound;
      await operation(request.signal, () => {
        if (!request.isCurrent()) throw new AuthIdentityChangedError();
      });
    } catch (error) {
      if (
        !bound?.signal.aborted &&
        !(error instanceof AuthIdentityChangedError)
      ) {
        const key = isPasskeyCancellation(error)
          ? "auth:passkeys.canceled"
          : error instanceof ApiError &&
              error.code === "passkeys_mock_unavailable"
            ? "auth:passkeys.mockUnavailable"
            : error instanceof ApiError && error.code === "httpsRequired"
              ? "auth:passkeys.httpsRequired"
              : error instanceof ApiError && error.status === 429
                ? "auth:passkeys.rateLimited"
                : "auth:passkeys.failed";
        setActionError(t(key));
      }
    } finally {
      bound?.dispose();
      busyRef.current = false;
      if (!lifecycle.current?.signal.aborted) setBusy(false);
    }
  };

  const refresh = async () => {
    await Promise.all([mutate(), mutateAll("/api/auth/verify")]);
  };

  const addPasskey = (event: React.FormEvent) => {
    event.preventDefault();
    if (!name.trim()) return;
    void run(async (signal, ensureCurrent) => {
      await retryAfterReauthentication(
        () => registerPasskey(name.trim(), signal),
        t("settings:system.reauthenticatePrompt"),
        signal,
      );
      ensureCurrent();
      setName("");
      addToast({ title: t("settings:security.added"), color: "success" });
      await refresh();
    });
  };

  const removePassword = () =>
    void run(async (signal, ensureCurrent) => {
      const confirmed = await confirmDialog(
        t("settings:security.removeConfirm"),
        {
          title: t("settings:security.removePassword"),
          confirmLabel: t("settings:security.verifyAndRemove"),
          destructive: true,
        },
      );
      ensureCurrent();
      if (!confirmed) return;
      await removePasswordWithPasskey(signal);
      ensureCurrent();
      addToast({
        title: t("settings:security.passwordRemoved"),
        color: "success",
      });
      await refresh();
    });

  return (
    <section>
      <SettingsSectionHeader
        title={t("settings:security.title")}
        description={t("settings:security.description")}
      />
      {!https || !supported ? (
        <p
          role="status"
          className="mb-5 rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm leading-body text-foreground"
        >
          {t(
            !https
              ? "auth:passkeys.httpsRequired"
              : "auth:passkeys.unsupported",
          )}
        </p>
      ) : null}
      {actionError ? (
        <p role="alert" className="mb-5 text-sm text-error">
          {actionError}
        </p>
      ) : null}
      {!https ? null : error ? (
        <div
          role="alert"
          className="rounded-lg border border-border bg-surface p-5"
        >
          <p className="text-sm text-error">
            {t("settings:security.loadFailed")}
          </p>
          <Button
            className="mt-3"
            variant="outline"
            onClick={() => void mutate()}
          >
            <RefreshCw size={16} />
            {t("settings:system.retry")}
          </Button>
        </div>
      ) : !data ? (
        <div className="flex justify-center py-12">
          <Spinner />
        </div>
      ) : (
        <div className="space-y-5">
          <Card
            icon={<Fingerprint size={18} />}
            title={t("settings:security.passkeysTitle")}
            description={t("settings:security.passkeysDescription")}
          >
            {data.passkeys.length === 0 ? (
              <p className="text-sm text-muted">
                {t("settings:security.noPasskeys")}
              </p>
            ) : (
              <ul className="divide-y divide-border">
                {data.passkeys.map((passkey) => (
                  <li
                    key={passkey.id}
                    className="flex items-start gap-3 py-3 first:pt-0"
                  >
                    <Fingerprint
                      className="mt-0.5 shrink-0 text-brand"
                      size={18}
                    />
                    <div className="min-w-0">
                      <p className="break-words text-sm font-medium text-foreground">
                        {passkey.name}
                      </p>
                      <p className="mt-1 text-xs text-muted">
                        {t("settings:security.createdAt", {
                          date: new Date(passkey.createdAt).toLocaleString(
                            i18n.language,
                          ),
                        })}
                      </p>
                      {passkey.lastUsedAt ? (
                        <p className="mt-1 text-xs text-muted">
                          {t("settings:security.lastUsedAt", {
                            date: new Date(passkey.lastUsedAt).toLocaleString(
                              i18n.language,
                            ),
                          })}
                        </p>
                      ) : null}
                    </div>
                  </li>
                ))}
              </ul>
            )}
            <form
              onSubmit={addPasskey}
              className="mt-5 space-y-3 border-t border-border pt-5"
            >
              <FormRow
                label={t("settings:security.passkeyName")}
                htmlFor="passkey-name"
              >
                <Input
                  id="passkey-name"
                  value={name}
                  maxLength={64}
                  required
                  disabled={busy || !supported}
                  placeholder={t("settings:security.namePlaceholder")}
                  onChange={(event) => setName(event.target.value)}
                />
              </FormRow>
              <Button
                type="submit"
                disabled={busy || !supported || !name.trim()}
              >
                {busy ? <Spinner className="h-4 w-4" /> : <Plus size={16} />}
                {t("settings:security.addPasskey")}
              </Button>
            </form>
          </Card>
          <Card
            icon={<KeyRound size={18} />}
            title={t("settings:security.passwordTitle")}
            description={t(
              data.hasPassword
                ? "settings:security.passwordEnabled"
                : "settings:security.passwordDisabled",
            )}
          >
            {data.hasPassword ? (
              <>
                <p className="text-sm leading-body text-muted">
                  {t(
                    data.passkeys.length
                      ? "settings:security.removeHelp"
                      : "settings:security.addFirst",
                  )}
                </p>
                <Button
                  color="danger"
                  className="mt-4"
                  disabled={busy || !supported || data.passkeys.length === 0}
                  onClick={removePassword}
                >
                  {t("settings:security.removePassword")}
                </Button>
              </>
            ) : (
              <p className="text-sm leading-body text-muted">
                {t("settings:security.passwordlessHelp")}
              </p>
            )}
          </Card>
        </div>
      )}
    </section>
  );
};
