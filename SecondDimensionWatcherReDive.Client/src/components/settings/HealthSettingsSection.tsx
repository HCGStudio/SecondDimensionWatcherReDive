import React from "react";
import { useTranslation } from "react-i18next";

import { Activity, Gauge, Timer } from "lucide-react";

import { apiErrorStatus } from "../../errors/apiError";
import { IncidentSettings, SystemSettings } from "../../settings/systemTypes";
import { isValidTimeSpan } from "../../settings/timeSpan";
import { useToast } from "../ToastProvider";
import { Card } from "../ui/Card";
import { FormRow } from "../ui/FormRow";
import { Input } from "../ui/Input";
import { SettingsSaveBar, SettingsSectionHeader } from "./SettingsControls";

const gibibyte = 1024 * 1024 * 1024;

export interface HealthSettingsSectionProps {
  value: IncidentSettings;
  onSave: (patch: { incidents: IncidentSettings }) => Promise<SystemSettings>;
}

export const HealthSettingsSection: React.FC<HealthSettingsSectionProps> = ({
  value,
  onSave,
}) => {
  const { t } = useTranslation("settings");
  const { addToast } = useToast();
  const [draft, setDraft] = React.useState<IncidentSettings>(() => ({
    ...value,
    disk: { ...value.disk },
  }));
  const [saving, setSaving] = React.useState(false);
  const [saved, setSaved] = React.useState(false);

  React.useEffect(
    () => setDraft({ ...value, disk: { ...value.disk } }),
    [value],
  );

  const dirty = JSON.stringify(draft) !== JSON.stringify(value);
  const invalid =
    !isValidTimeSpan(draft.downloadStalledAfter, 1) ||
    !isValidTimeSpan(draft.reportThrottle, 1) ||
    !isValidTimeSpan(draft.reconciliationInterval, 10) ||
    !Number.isSafeInteger(draft.disk.minimumAvailableBytes) ||
    draft.disk.minimumAvailableBytes < 0 ||
    !Number.isFinite(draft.disk.minimumAvailablePercent) ||
    draft.disk.minimumAvailablePercent < 0 ||
    draft.disk.minimumAvailablePercent > 100;

  const reset = React.useCallback(() => {
    setDraft({ ...value, disk: { ...value.disk } });
    setSaved(false);
  }, [value]);

  const save = React.useCallback(async () => {
    if (saving || invalid) {
      if (invalid)
        addToast({
          title: t("system.health.validationFailed"),
          color: "warning",
        });
      return;
    }
    setSaving(true);
    setSaved(false);
    try {
      await onSave({ incidents: draft });
      setSaved(true);
      addToast({ title: t("system.health.saved"), color: "success" });
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

  const updateTime = React.useCallback(
    (
      field:
        "downloadStalledAfter" | "reportThrottle" | "reconciliationInterval",
      nextValue: string,
    ) => setDraft((current) => ({ ...current, [field]: nextValue })),
    [],
  );

  return (
    <section>
      <SettingsSectionHeader
        eyebrow={t("system.health.eyebrow")}
        title={t("system.health.title")}
        description={t("system.health.description")}
      />

      <Card
        icon={<Activity size={18} />}
        title={t("system.health.download.title")}
        description={t("system.health.download.description")}
      >
        <div className="grid gap-5 sm:grid-cols-2">
          <TimeSpanField
            label={t("system.health.download.stalledAfter")}
            value={draft.downloadStalledAfter}
            minimumSeconds={1}
            onChange={(nextValue) =>
              updateTime("downloadStalledAfter", nextValue)
            }
          />
          <TimeSpanField
            label={t("system.health.download.reportThrottle")}
            value={draft.reportThrottle}
            minimumSeconds={1}
            onChange={(nextValue) => updateTime("reportThrottle", nextValue)}
          />
        </div>
      </Card>

      <Card
        className="mt-5"
        icon={<Timer size={18} />}
        title={t("system.health.reconciliation.title")}
        description={t("system.health.reconciliation.description")}
      >
        <div className="max-w-sm">
          <TimeSpanField
            label={t("system.health.reconciliation.interval")}
            value={draft.reconciliationInterval}
            minimumSeconds={10}
            onChange={(nextValue) =>
              updateTime("reconciliationInterval", nextValue)
            }
          />
        </div>
      </Card>

      <Card
        className="mt-5"
        icon={<Gauge size={18} />}
        title={t("system.health.disk.title")}
        description={t("system.health.disk.description")}
      >
        <div className="grid gap-5 sm:grid-cols-2">
          <FormRow label={t("system.health.disk.minimumBytes")}>
            <Input
              type="number"
              min={0}
              step={0.1}
              value={draft.disk.minimumAvailableBytes / gibibyte}
              isInvalid={
                !Number.isSafeInteger(draft.disk.minimumAvailableBytes) ||
                draft.disk.minimumAvailableBytes < 0
              }
              onChange={(event) =>
                setDraft((current) => ({
                  ...current,
                  disk: {
                    ...current.disk,
                    minimumAvailableBytes: Math.round(
                      Number(event.target.value) * gibibyte,
                    ),
                  },
                }))
              }
            />
          </FormRow>
          <FormRow label={t("system.health.disk.minimumPercent")}>
            <Input
              type="number"
              min={0}
              max={100}
              step={0.1}
              value={draft.disk.minimumAvailablePercent}
              isInvalid={
                !Number.isFinite(draft.disk.minimumAvailablePercent) ||
                draft.disk.minimumAvailablePercent < 0 ||
                draft.disk.minimumAvailablePercent > 100
              }
              onChange={(event) =>
                setDraft((current) => ({
                  ...current,
                  disk: {
                    ...current.disk,
                    minimumAvailablePercent: Number(event.target.value),
                  },
                }))
              }
            />
          </FormRow>
        </div>
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

interface TimeSpanFieldProps {
  label: string;
  value: string;
  minimumSeconds: number;
  onChange: (value: string) => void;
}

const TimeSpanField: React.FC<TimeSpanFieldProps> = ({
  label,
  value,
  minimumSeconds,
  onChange,
}) => (
  <FormRow label={label} isInvalid={!isValidTimeSpan(value, minimumSeconds)}>
    <Input
      className="font-mono"
      value={value}
      placeholder="00:05:00"
      isInvalid={!isValidTimeSpan(value, minimumSeconds)}
      onChange={(event) => onChange(event.target.value)}
    />
  </FormRow>
);
