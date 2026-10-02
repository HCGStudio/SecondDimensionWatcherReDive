import React from "react";
import { useTranslation } from "react-i18next";

import { IAuthProfile } from "../auth/IAuthResult";
import {
  AuthBoundRequest,
  AuthIdentityChangedError,
  beginAuthBoundRequest,
} from "../auth/httpClient";
import { retryAfterReauthentication } from "../auth/utils";
import { Button } from "../components/ui/Button";
import { Card } from "../components/ui/Card";
import { FormRow } from "../components/ui/FormRow";
import { Input } from "../components/ui/Input";
import { PasswordInput } from "../components/ui/PasswordInput";
import { ProfileAvatar } from "./ProfileAvatar";
import {
  removeProfileAvatar,
  renameProfile,
  setProfilePin,
  uploadProfileAvatar,
} from "./api";

export const ProfileSettings: React.FC<{
  profile: IAuthProfile;
  onSaved: () => Promise<unknown>;
}> = ({ profile, onSaved }) => {
  const { t } = useTranslation("accounts");
  const [name, setName] = React.useState(profile.name);
  const [file, setFile] = React.useState<File | null>(null);
  const [preview, setPreview] = React.useState<string>();
  const [pin, setPin] = React.useState("");
  const [currentPin, setCurrentPin] = React.useState("");
  const [busy, setBusy] = React.useState(false);
  const [message, setMessage] = React.useState<{
    text: string;
    error: boolean;
  } | null>(null);
  const fileInput = React.useRef<HTMLInputElement>(null);
  const controlId = React.useId();
  const pendingRequest = React.useRef<AuthBoundRequest | null>(null);
  React.useEffect(() => () => pendingRequest.current?.abort(), []);
  const ensureCurrent = (bound: AuthBoundRequest) => {
    if (!bound.isCurrent()) throw new AuthIdentityChangedError();
  };
  React.useEffect(() => setName(profile.name), [profile.name]);
  React.useEffect(() => {
    if (!file) {
      setPreview(undefined);
      return;
    }
    const url = URL.createObjectURL(file);
    setPreview(url);
    return () => URL.revokeObjectURL(url);
  }, [file]);
  const run = async (
    operation: (bound: AuthBoundRequest) => Promise<unknown>,
  ) => {
    if (pendingRequest.current) return;
    setBusy(true);
    setMessage(null);
    let bound: AuthBoundRequest | undefined;
    try {
      bound = beginAuthBoundRequest(true);
      pendingRequest.current = bound;
      ensureCurrent(bound);
      await operation(bound);
      ensureCurrent(bound);
      await onSaved();
      ensureCurrent(bound);
      setMessage({ text: t("saved"), error: false });
    } catch (error) {
      if (bound && !bound.isCurrent()) return;
      setMessage({
        text: error instanceof Error ? error.message : t("failed"),
        error: true,
      });
    } finally {
      bound?.dispose();
      pendingRequest.current = null;
      setBusy(false);
    }
  };
  const savePin = async (value: string | null, bound: AuthBoundRequest) => {
    await retryAfterReauthentication(() => {
      ensureCurrent(bound);
      return setProfilePin(profile.id, value, currentPin, bound.signal);
    }, t("reauthPrompt"));
    ensureCurrent(bound);
    setPin("");
    setCurrentPin("");
  };
  const clearFile = () => {
    setFile(null);
    if (fileInput.current) fileInput.current.value = "";
  };
  return (
    <Card
      className="mt-5"
      title={t("profileSettings")}
      description={profile.name}
    >
      <fieldset disabled={busy} className="space-y-6">
        <form
          className="flex flex-wrap items-end gap-3"
          onSubmit={(event) => {
            event.preventDefault();
            void run((bound) =>
              renameProfile(profile.id, name.trim(), bound.signal),
            );
          }}
        >
          <FormRow
            label={t("profileName")}
            htmlFor={`${controlId}-name`}
            className="min-w-0 flex-1"
          >
            <Input
              id={`${controlId}-name`}
              value={name}
              maxLength={64}
              required
              onChange={(event) => setName(event.target.value)}
            />
          </FormRow>
          <Button
            type="submit"
            disabled={busy || !name.trim() || name.trim() === profile.name}
          >
            {t("saveName")}
          </Button>
        </form>
        <form
          className="space-y-3 border-t border-border-light pt-5"
          onSubmit={(event) => {
            event.preventDefault();
            void run(async (bound) => {
              if (!file) return;
              if (
                file.size === 0 ||
                file.size > 2 * 1024 * 1024 ||
                !["image/png", "image/jpeg"].includes(file.type)
              )
                throw new Error(t("avatarInvalid"));
              let bitmap: ImageBitmap;
              try {
                bitmap = await createImageBitmap(file);
              } catch {
                throw new Error(t("avatarInvalid"));
              }
              const valid = bitmap.width <= 4096 && bitmap.height <= 4096;
              bitmap.close();
              if (!valid) throw new Error(t("avatarInvalid"));
              ensureCurrent(bound);
              await uploadProfileAvatar(profile.id, file, bound.signal);
              ensureCurrent(bound);
              clearFile();
            });
          }}
        >
          <div className="flex items-center gap-4">
            <ProfileAvatar
              src={preview ?? profile.avatar}
              className="h-16 w-16"
            />
            <FormRow
              label={t("avatar")}
              htmlFor={`${controlId}-avatar`}
              className="min-w-0 flex-1"
            >
              <Input
                id={`${controlId}-avatar`}
                ref={fileInput}
                type="file"
                accept="image/png,image/jpeg"
                onChange={(event) => setFile(event.target.files?.[0] ?? null)}
              />
            </FormRow>
          </div>
          <p className="text-sm text-muted">{t("avatarHelp")}</p>
          <div className="flex flex-wrap gap-3">
            <Button type="submit" disabled={busy || !file}>
              {t("uploadAvatar")}
            </Button>
            {file ? (
              <Button type="button" variant="outline" onClick={clearFile}>
                {t("cancelSelection")}
              </Button>
            ) : null}
            {profile.avatar ? (
              <Button
                type="button"
                variant="outline"
                disabled={busy}
                onClick={() =>
                  void run(async (bound) => {
                    await removeProfileAvatar(profile.id, bound.signal);
                    ensureCurrent(bound);
                    clearFile();
                  })
                }
              >
                {t("removeAvatar")}
              </Button>
            ) : null}
          </div>
        </form>
        <form
          className="space-y-3 border-t border-border-light pt-5"
          onSubmit={(event) => {
            event.preventDefault();
            void run((bound) => savePin(pin, bound));
          }}
        >
          <h4 className="font-medium text-foreground">{t("pinSettings")}</h4>
          <p className="text-sm text-muted">
            {profile.hasPin ? t("pinProtected") : t("noPin")}
          </p>
          <div className="grid gap-3 md:grid-cols-2">
            {profile.hasPin ? (
              <FormRow
                label={t("currentPin")}
                htmlFor={`${controlId}-current-pin`}
              >
                <PasswordInput
                  id={`${controlId}-current-pin`}
                  value={currentPin}
                  inputMode="numeric"
                  maxLength={8}
                  autoComplete="off"
                  onChange={(event) => setCurrentPin(event.target.value)}
                />
              </FormRow>
            ) : null}
            <FormRow label={t("newPin")} htmlFor={`${controlId}-new-pin`}>
              <PasswordInput
                id={`${controlId}-new-pin`}
                value={pin}
                inputMode="numeric"
                pattern="[0-9]{4,8}"
                maxLength={8}
                required
                autoComplete="new-password"
                onChange={(event) => setPin(event.target.value)}
              />
            </FormRow>
          </div>
          <p className="text-sm text-muted">{t("pinHelp")}</p>
          <div className="flex flex-wrap gap-3">
            <Button type="submit" disabled={busy || !/^[0-9]{4,8}$/.test(pin)}>
              {t("savePin")}
            </Button>
            {profile.hasPin ? (
              <Button
                type="button"
                variant="outline"
                disabled={busy}
                onClick={() => void run((bound) => savePin(null, bound))}
              >
                {t("removePin")}
              </Button>
            ) : null}
          </div>
        </form>
      </fieldset>
      {message ? (
        <p
          role="status"
          aria-live="polite"
          className={`mt-4 text-sm ${message.error ? "text-error" : "text-muted"}`}
        >
          {message.text}
        </p>
      ) : null}
    </Card>
  );
};
