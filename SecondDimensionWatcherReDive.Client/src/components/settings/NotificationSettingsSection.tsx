import React from "react";
import { useTranslation } from "react-i18next";

import { BellRing, Clock3, History, Send, Webhook } from "lucide-react";

import { sendTestNotification } from "../../notifications/api";
import { useNotificationDeliveries } from "../../notifications/hooks";
import {
  NotificationEventType,
  NotificationSettings,
  SecretDraft,
  SystemSettings,
  createSecretDraft,
  toSecretMutation,
} from "../../settings/systemTypes";
import { useToast } from "../ToastProvider";
import { Button } from "../ui/Button";
import { Card } from "../ui/Card";
import { FormRow } from "../ui/FormRow";
import { Input } from "../ui/Input";
import {
  SecretField,
  SettingsSaveBar,
  SettingsSectionHeader,
  ToggleField,
} from "./SettingsControls";

const eventTypes: NotificationEventType[] = [
  "releaseMatched",
  "downloadPendingConfirmation",
  "downloadCompleted",
  "downloadFailed",
  "incidentOpened",
  "metadataNeedsReview",
  "diskSpaceLow",
];

export interface NotificationSettingsSectionProps {
  value: NotificationSettings;
  onSave: (patch: {
    notifications: Omit<NotificationSettings, "webhookUrl"> & {
      webhookUrl: ReturnType<typeof toSecretMutation>;
    };
  }) => Promise<SystemSettings>;
}

export const NotificationSettingsSection: React.FC<
  NotificationSettingsSectionProps
> = ({ value, onSave }) => {
  const { t, i18n } = useTranslation("settings");
  const { addToast } = useToast();
  const { data: deliveries, mutate: mutateDeliveries } =
    useNotificationDeliveries();
  const [draft, setDraft] = React.useState<NotificationSettings>(() => ({
    ...value,
    events: [...value.events],
  }));
  const [urlDraft, setUrlDraft] =
    React.useState<SecretDraft>(createSecretDraft);
  const [saving, setSaving] = React.useState(false);
  const [testing, setTesting] = React.useState(false);
  const [saved, setSaved] = React.useState(false);

  React.useEffect(() => {
    setDraft({ ...value, events: [...value.events] });
    setUrlDraft(createSecretDraft());
  }, [value]);

  const secretMutation = toSecretMutation(urlDraft);
  const dirty =
    JSON.stringify(draft) !== JSON.stringify(value) || secretMutation !== null;
  const quietPairValid =
    (draft.quietHoursStart === null && draft.quietHoursEnd === null) ||
    (Boolean(draft.quietHoursStart) && Boolean(draft.quietHoursEnd));
  const invalid =
    draft.events.length === 0 ||
    !draft.timeZoneId.trim() ||
    !quietPairValid ||
    (draft.webhookEnabled &&
      !value.webhookUrl.isConfigured &&
      !urlDraft.value.trim()) ||
    (draft.webhookEnabled && urlDraft.operation === "clear");

  const reset = React.useCallback(() => {
    setDraft({ ...value, events: [...value.events] });
    setUrlDraft(createSecretDraft());
    setSaved(false);
  }, [value]);

  const save = React.useCallback(async () => {
    if (invalid || saving) {
      if (invalid)
        addToast({
          title: t("system.notifications.validationFailed"),
          color: "warning",
        });
      return;
    }
    setSaving(true);
    setSaved(false);
    try {
      await onSave({
        notifications: {
          webhookEnabled: draft.webhookEnabled,
          events: draft.events,
          quietHoursStart: draft.quietHoursStart || null,
          quietHoursEnd: draft.quietHoursEnd || null,
          timeZoneId: draft.timeZoneId,
          webhookUrl: secretMutation,
        },
      });
      setSaved(true);
      addToast({
        title: t("system.notifications.saved"),
        color: "success",
      });
    } catch (error) {
      addToast({
        title:
          error instanceof Error && error.message === "409"
            ? t("system.save.conflict")
            : t("system.save.failed"),
        color: "danger",
      });
    } finally {
      setSaving(false);
    }
  }, [addToast, draft, invalid, onSave, saving, secretMutation, t]);

  const test = React.useCallback(async () => {
    setTesting(true);
    try {
      await sendTestNotification();
      await mutateDeliveries();
      addToast({
        title: t("system.notifications.testQueued"),
        color: "success",
      });
    } catch {
      addToast({
        title: t("system.notifications.testFailed"),
        color: "danger",
      });
    } finally {
      setTesting(false);
    }
  }, [addToast, mutateDeliveries, t]);

  return (
    <section>
      <SettingsSectionHeader
        eyebrow={t("system.notifications.eyebrow")}
        title={t("system.notifications.title")}
        description={t("system.notifications.description")}
      />

      <Card
        icon={<Webhook size={18} />}
        title={t("system.notifications.webhook.title")}
        description={t("system.notifications.webhook.description")}
      >
        <div className="space-y-5">
          <ToggleField
            checked={draft.webhookEnabled}
            label={t("system.notifications.webhook.enabled")}
            description={t("system.notifications.webhook.enabledHelp")}
            onChange={(webhookEnabled) =>
              setDraft((current) => ({ ...current, webhookEnabled }))
            }
          />
          <SecretField
            id="settings-notification-webhook-url"
            label={t("system.notifications.webhook.url")}
            state={value.webhookUrl}
            draft={urlDraft}
            help={t("system.notifications.webhook.secretHelp")}
            onChange={setUrlDraft}
          />
          <Button
            type="button"
            variant="outline"
            disabled={testing || dirty || !value.webhookEnabled}
            onClick={() => void test()}
          >
            <Send size={15} />
            {testing
              ? t("system.notifications.webhook.testing")
              : t("system.notifications.webhook.test")}
          </Button>
        </div>
      </Card>

      <Card
        className="mt-5"
        icon={<BellRing size={18} />}
        title={t("system.notifications.events.title")}
        description={t("system.notifications.events.description")}
      >
        <div className="grid gap-3 sm:grid-cols-2">
          {eventTypes.map((eventType) => (
            <ToggleField
              key={eventType}
              checked={draft.events.includes(eventType)}
              label={t(`system.notifications.events.items.${eventType}`)}
              onChange={(checked) =>
                setDraft((current) => ({
                  ...current,
                  events: checked
                    ? [...current.events, eventType]
                    : current.events.filter((item) => item !== eventType),
                }))
              }
            />
          ))}
        </div>
      </Card>

      <Card
        className="mt-5"
        icon={<Clock3 size={18} />}
        title={t("system.notifications.quiet.title")}
        description={t("system.notifications.quiet.description")}
      >
        <div className="grid gap-5 sm:grid-cols-3">
          <FormRow label={t("system.notifications.quiet.start")}>
            <Input
              placeholder="22:00:00"
              value={draft.quietHoursStart ?? ""}
              onChange={(event) =>
                setDraft((current) => ({
                  ...current,
                  quietHoursStart: event.target.value || null,
                }))
              }
            />
          </FormRow>
          <FormRow label={t("system.notifications.quiet.end")}>
            <Input
              placeholder="08:00:00"
              value={draft.quietHoursEnd ?? ""}
              onChange={(event) =>
                setDraft((current) => ({
                  ...current,
                  quietHoursEnd: event.target.value || null,
                }))
              }
            />
          </FormRow>
          <FormRow label={t("system.notifications.quiet.timeZone")}>
            <Input
              value={draft.timeZoneId}
              placeholder="Asia/Shanghai"
              onChange={(event) =>
                setDraft((current) => ({
                  ...current,
                  timeZoneId: event.target.value,
                }))
              }
            />
          </FormRow>
        </div>
      </Card>

      <Card
        className="mt-5"
        icon={<History size={18} />}
        title={t("system.notifications.delivery.title")}
        description={t("system.notifications.delivery.description")}
      >
        {!deliveries?.length ? (
          <p className="text-sm text-muted">
            {t("system.notifications.delivery.empty")}
          </p>
        ) : (
          <ul className="divide-y divide-border-light">
            {deliveries.map((delivery) => (
              <li
                key={delivery.id}
                className="flex flex-col gap-1 py-3 text-sm sm:flex-row sm:items-center sm:justify-between"
              >
                <div>
                  <span className="font-medium text-foreground">
                    {t(`system.notifications.events.items.${delivery.type}`, {
                      defaultValue: delivery.type,
                    })}
                  </span>
                  {delivery.lastError ? (
                    <p className="text-xs text-error">{delivery.lastError}</p>
                  ) : null}
                </div>
                <span className="text-xs text-muted">
                  {t(`system.notifications.delivery.status.${delivery.status}`)}{" "}
                  ·{" "}
                  {new Date(delivery.occurredAt).toLocaleString(
                    i18n.resolvedLanguage,
                  )}
                </span>
              </li>
            ))}
          </ul>
        )}
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
