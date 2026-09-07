import React from "react";
import { useTranslation } from "react-i18next";

import { Download, HardDrive } from "lucide-react";

import { apiErrorStatus } from "../../errors/apiError";
import {
  SecretDraft,
  SystemSettings,
  TorrentSettings,
  TorrentSettingsPatch,
  createSecretDraft,
  toSecretMutation,
} from "../../settings/systemTypes";
import {
  isHttpEndpoint,
  requiresCredentialChange,
} from "../../settings/validation";
import { useToast } from "../ToastProvider";
import { Card } from "../ui/Card";
import { FormRow } from "../ui/FormRow";
import { Input } from "../ui/Input";
import {
  SecretField,
  SettingsSaveBar,
  SettingsSectionHeader,
} from "./SettingsControls";

interface DownloadDraft extends Omit<TorrentSettings, "password"> {
  password: SecretDraft;
}

const createDraft = (value: TorrentSettings): DownloadDraft => ({
  url: value.url,
  userName: value.userName,
  userAgent: value.userAgent,
  password: createSecretDraft(),
});

export interface DownloadSettingsSectionProps {
  value: TorrentSettings;
  onSave: (patch: { torrent: TorrentSettingsPatch }) => Promise<SystemSettings>;
}

export const DownloadSettingsSection: React.FC<
  DownloadSettingsSectionProps
> = ({ value, onSave }) => {
  const { t } = useTranslation("settings");
  const { addToast } = useToast();
  const [draft, setDraft] = React.useState(() => createDraft(value));
  const [saving, setSaving] = React.useState(false);
  const [saved, setSaved] = React.useState(false);

  React.useEffect(() => setDraft(createDraft(value)), [value]);

  const dirty =
    draft.url !== value.url ||
    draft.userName !== value.userName ||
    draft.userAgent !== value.userAgent ||
    draft.password.operation !== "keep" ||
    draft.password.value.trim().length > 0;
  const credentialRequired = requiresCredentialChange(
    value.url,
    draft.url,
    value.password,
    draft.password,
  );
  const invalid = !isHttpEndpoint(draft.url) || credentialRequired;

  const reset = React.useCallback(() => {
    setDraft(createDraft(value));
    setSaved(false);
  }, [value]);

  const save = React.useCallback(async () => {
    if (saving || invalid) {
      if (invalid)
        addToast({
          title: t("system.downloads.invalidUrl"),
          color: "warning",
        });
      return;
    }
    setSaving(true);
    setSaved(false);
    try {
      await onSave({
        torrent: {
          url: draft.url.trim(),
          userName: draft.userName.trim(),
          userAgent: draft.userAgent.trim(),
          password: toSecretMutation(draft.password),
        },
      });
      setSaved(true);
      addToast({ title: t("system.downloads.saved"), color: "success" });
    } catch (error) {
      addToast({
        title:
          apiErrorStatus(error) === 409
            ? t("system.save.conflict")
            : t("system.save.failed"),
        color: "danger",
      });
    } finally {
      setSaving(false);
    }
  }, [addToast, draft, invalid, onSave, saving, t]);

  return (
    <section>
      <SettingsSectionHeader
        eyebrow={t("system.downloads.eyebrow")}
        title={t("system.downloads.title")}
        description={t("system.downloads.description")}
      />
      <Card
        icon={<Download size={18} />}
        title={t("system.downloads.qbittorrent.title")}
        description={t("system.downloads.qbittorrent.description")}
      >
        <div className="grid gap-5 sm:grid-cols-2">
          <FormRow label={t("system.downloads.qbittorrent.url")}>
            <Input
              type="url"
              value={draft.url}
              placeholder="http://localhost:8080"
              isInvalid={!isHttpEndpoint(draft.url)}
              onChange={(event) =>
                setDraft((current) => ({
                  ...current,
                  url: event.target.value,
                }))
              }
            />
            {credentialRequired ? (
              <p className="mt-1 text-xs text-warning">
                {t("system.downloads.originCredentialRequired")}
              </p>
            ) : null}
          </FormRow>
          <FormRow label={t("system.downloads.qbittorrent.userName")}>
            <Input
              value={draft.userName}
              autoComplete="username"
              onChange={(event) =>
                setDraft((current) => ({
                  ...current,
                  userName: event.target.value,
                }))
              }
            />
          </FormRow>
          <SecretField
            id="settings-qbittorrent-password"
            label={t("system.downloads.qbittorrent.password")}
            state={value.password}
            draft={draft.password}
            onChange={(password) =>
              setDraft((current) => ({ ...current, password }))
            }
          />
          <FormRow label={t("system.downloads.qbittorrent.userAgent")}>
            <Input
              value={draft.userAgent}
              onChange={(event) =>
                setDraft((current) => ({
                  ...current,
                  userAgent: event.target.value,
                }))
              }
            />
          </FormRow>
        </div>
      </Card>

      <Card
        className="mt-5"
        icon={<HardDrive size={18} />}
        title={t("system.downloads.storage.title")}
        description={t("system.downloads.storage.description")}
      >
        <p className="text-xs leading-body text-subtle">
          {t("system.downloads.storage.readOnlyHelp")}
        </p>
      </Card>

      <SettingsSaveBar
        dirty={dirty}
        saving={saving}
        saved={saved}
        onReset={reset}
        onSave={() => void save()}
      />
    </section>
  );
};
