import React from "react";
import { useTranslation } from "react-i18next";

import { Network, RadioTower } from "lucide-react";

import { apiErrorStatus } from "../../errors/apiError";
import {
  NfsSettings,
  NfsSettingsPatch,
  SystemSettings,
} from "../../settings/systemTypes";
import { isIntegerInRange, isIpAddress } from "../../settings/validation";
import { useToast } from "../ToastProvider";
import { Card } from "../ui/Card";
import { FormRow } from "../ui/FormRow";
import { Input } from "../ui/Input";
import {
  RestartNotice,
  SettingsSaveBar,
  SettingsSectionHeader,
  ToggleField,
} from "./SettingsControls";
import { WebDavSettingsSection } from "./WebDavSettingsSection";

const editableNfs = (value: NfsSettings): NfsSettingsPatch => ({
  enabled: value.enabled,
  port: value.port,
  bindAddress: value.bindAddress,
  leaseSeconds: value.leaseSeconds,
  maxConnections: value.maxConnections,
});

export interface AccessSettingsSectionProps {
  value: NfsSettings;
  onSave: (patch: { nfs: NfsSettingsPatch }) => Promise<SystemSettings>;
}

export const AccessSettingsSection: React.FC<AccessSettingsSectionProps> = ({
  value,
  onSave,
}) => {
  const { t } = useTranslation("settings");
  const { addToast } = useToast();
  const [draft, setDraft] = React.useState<NfsSettingsPatch>(() =>
    editableNfs(value),
  );
  const [saving, setSaving] = React.useState(false);
  const [saved, setSaved] = React.useState(false);

  React.useEffect(() => setDraft(editableNfs(value)), [value]);

  const dirty = JSON.stringify(draft) !== JSON.stringify(editableNfs(value));
  const invalid =
    !isIntegerInRange(draft.port, 0, 65_535) ||
    !isIpAddress(draft.bindAddress) ||
    !isIntegerInRange(draft.leaseSeconds, 1, 2_147_483_647) ||
    !isIntegerInRange(draft.maxConnections, 1, 2_147_483_647);

  const reset = React.useCallback(() => {
    setDraft(editableNfs(value));
    setSaved(false);
  }, [value]);

  const save = React.useCallback(async () => {
    if (saving || invalid) {
      if (invalid)
        addToast({
          title: t("system.access.validationFailed"),
          color: "warning",
        });
      return;
    }
    setSaving(true);
    setSaved(false);
    try {
      await onSave({
        nfs: { ...draft, bindAddress: draft.bindAddress.trim() },
      });
      setSaved(true);
      addToast({ title: t("system.access.saved"), color: "success" });
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
        eyebrow={t("system.access.eyebrow")}
        title={t("system.access.title")}
        description={t("system.access.description")}
      />
      <Card
        icon={<Network size={18} />}
        title={t("system.access.nfs.title")}
        description={t("system.access.nfs.description")}
      >
        <ToggleField
          checked={draft.enabled}
          label={t("system.access.nfs.enabled")}
          description={t("system.access.nfs.enabledHelp")}
          onChange={(enabled) =>
            setDraft((current) => ({ ...current, enabled }))
          }
        />
        <div className="mt-5 grid gap-5 sm:grid-cols-2">
          <FormRow label={t("system.access.nfs.bindAddress")}>
            <Input
              value={draft.bindAddress}
              isInvalid={!isIpAddress(draft.bindAddress)}
              onChange={(event) =>
                setDraft((current) => ({
                  ...current,
                  bindAddress: event.target.value,
                }))
              }
            />
          </FormRow>
          <FormRow label={t("system.access.nfs.port")}>
            <Input
              type="number"
              min={0}
              max={65535}
              value={draft.port}
              isInvalid={!isIntegerInRange(draft.port, 0, 65_535)}
              onChange={(event) =>
                setDraft((current) => ({
                  ...current,
                  port: Number(event.target.value),
                }))
              }
            />
          </FormRow>
          <FormRow label={t("system.access.nfs.leaseSeconds")}>
            <Input
              type="number"
              min={1}
              max={2_147_483_647}
              value={draft.leaseSeconds}
              isInvalid={
                !isIntegerInRange(draft.leaseSeconds, 1, 2_147_483_647)
              }
              onChange={(event) =>
                setDraft((current) => ({
                  ...current,
                  leaseSeconds: Number(event.target.value),
                }))
              }
            />
          </FormRow>
          <FormRow label={t("system.access.nfs.maxConnections")}>
            <Input
              type="number"
              min={1}
              max={2_147_483_647}
              value={draft.maxConnections}
              isInvalid={
                !isIntegerInRange(draft.maxConnections, 1, 2_147_483_647)
              }
              onChange={(event) =>
                setDraft((current) => ({
                  ...current,
                  maxConnections: Number(event.target.value),
                }))
              }
            />
          </FormRow>
        </div>
        <div className="mt-5">
          <RestartNotice>
            <span className="inline-flex items-start gap-2">
              <RadioTower size={16} className="mt-1 shrink-0 text-warning" />
              <span>{t("system.access.nfs.restartHelp")}</span>
            </span>
          </RestartNotice>
        </div>
      </Card>

      <SettingsSaveBar
        dirty={dirty}
        saving={saving}
        saved={saved}
        requiresRestart={value.pendingRestart}
        onReset={reset}
        onSave={() => void save()}
      />

      <WebDavSettingsSection />
    </section>
  );
};
