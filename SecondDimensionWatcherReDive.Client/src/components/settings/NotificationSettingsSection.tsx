import React from "react";
import { useTranslation } from "react-i18next";

import {
  BellRing,
  Clock3,
  History,
  MonitorSmartphone,
  Send,
  Trash2,
  Webhook,
} from "lucide-react";

import { apiErrorStatus } from "../../errors/apiError";
import {
  removeWebPushSubscription,
  sendTestNotification,
} from "../../notifications/api";
import {
  useNotificationDeliveries,
  useWebPushSubscriptions,
} from "../../notifications/hooks";
import {
  disableWebPushForCurrentDevice,
  enableWebPushForCurrentDevice,
  getCurrentWebPushSubscription,
  isWebPushSupported,
} from "../../notifications/webPush";
import {
  NotificationEventType,
  NotificationSettings,
  NotificationSettingsPatch,
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
    notifications: NotificationSettingsPatch;
  }) => Promise<SystemSettings>;
}

export const NotificationSettingsSection: React.FC<
  NotificationSettingsSectionProps
> = ({ value, onSave }) => {
  const { t, i18n } = useTranslation("settings");
  const { addToast } = useToast();
  const { data: deliveries, mutate: mutateDeliveries } =
    useNotificationDeliveries();
  const { data: subscriptions, mutate: mutateSubscriptions } =
    useWebPushSubscriptions();
  const [draft, setDraft] = React.useState<NotificationSettings>(() => ({
    ...value,
    events: [...value.events],
  }));
  const [urlDraft, setUrlDraft] =
    React.useState<SecretDraft>(createSecretDraft);
  const [saving, setSaving] = React.useState(false);
  const [testing, setTesting] = React.useState(false);
  const [webPushBusy, setWebPushBusy] = React.useState(false);
  const [deviceSubscribed, setDeviceSubscribed] = React.useState(false);
  const [saved, setSaved] = React.useState(false);

  React.useEffect(() => {
    let active = true;
    void getCurrentWebPushSubscription()
      .then((subscription) => {
        if (active) setDeviceSubscribed(subscription !== null);
      })
      .catch(() => {
        if (active) setDeviceSubscribed(false);
      });
    return () => {
      active = false;
    };
  }, []);

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
  const webPushInvalid = draft.webPushEnabled && !draft.webPushSubject.trim();

  const reset = React.useCallback(() => {
    setDraft({ ...value, events: [...value.events] });
    setUrlDraft(createSecretDraft());
    setSaved(false);
  }, [value]);

  const save = React.useCallback(async () => {
    if (invalid || webPushInvalid || saving) {
      if (invalid || webPushInvalid)
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
          webPushEnabled: draft.webPushEnabled,
          webPushSubject: draft.webPushSubject,
          events: draft.events,
          quietHoursStart: draft.quietHoursStart || null,
          quietHoursEnd: draft.quietHoursEnd || null,
          timeZoneId: draft.timeZoneId,
          webhookUrl: secretMutation,
          generateVapidKeys:
            draft.webPushEnabled &&
            (!value.vapidPublicKey || !value.vapidPrivateKey.isConfigured),
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
          apiErrorStatus(error) === 409
            ? t("system.save.conflict")
            : t("system.save.failed"),
        color: "danger",
      });
    } finally {
      setSaving(false);
    }
  }, [
    addToast,
    draft,
    invalid,
    onSave,
    saving,
    secretMutation,
    t,
    value.vapidPrivateKey.isConfigured,
    value.vapidPublicKey,
    webPushInvalid,
  ]);

  const enableCurrentDevice = React.useCallback(async () => {
    if (webPushBusy || !value.vapidPublicKey) return;
    setWebPushBusy(true);
    try {
      await enableWebPushForCurrentDevice(value.vapidPublicKey);
      setDeviceSubscribed(true);
      await mutateSubscriptions();
      addToast({
        title: t("system.notifications.webPush.deviceEnabled"),
        color: "success",
      });
    } catch {
      addToast({
        title: t("system.notifications.webPush.deviceFailed"),
        color: "danger",
      });
    } finally {
      setWebPushBusy(false);
    }
  }, [addToast, mutateSubscriptions, t, value.vapidPublicKey, webPushBusy]);

  const disableCurrentDevice = React.useCallback(async () => {
    if (webPushBusy) return;
    setWebPushBusy(true);
    try {
      await disableWebPushForCurrentDevice();
      setDeviceSubscribed(false);
      await mutateSubscriptions();
      addToast({
        title: t("system.notifications.webPush.deviceDisabled"),
        color: "success",
      });
    } catch {
      addToast({
        title: t("system.notifications.webPush.deviceFailed"),
        color: "danger",
      });
    } finally {
      setWebPushBusy(false);
    }
  }, [addToast, mutateSubscriptions, t, webPushBusy]);

  const revokeSubscription = React.useCallback(
    async (id: string) => {
      if (webPushBusy) return;
      setWebPushBusy(true);
      try {
        await removeWebPushSubscription(id);
        await mutateSubscriptions();
        addToast({
          title: t("system.notifications.webPush.subscriptionRevoked"),
          color: "success",
        });
      } catch {
        addToast({
          title: t("system.notifications.webPush.deviceFailed"),
          color: "danger",
        });
      } finally {
        setWebPushBusy(false);
      }
    },
    [addToast, mutateSubscriptions, t, webPushBusy],
  );

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
        </div>
      </Card>

      <Card
        className="mt-5"
        icon={<MonitorSmartphone size={18} />}
        title={t("system.notifications.webPush.title")}
        description={t("system.notifications.webPush.description")}
      >
        <div className="space-y-5">
          <ToggleField
            checked={draft.webPushEnabled}
            label={t("system.notifications.webPush.enabled")}
            description={t("system.notifications.webPush.enabledHelp")}
            onChange={(webPushEnabled) =>
              setDraft((current) => ({ ...current, webPushEnabled }))
            }
          />
          <FormRow label={t("system.notifications.webPush.subject")}>
            <Input
              placeholder="mailto:admin@example.com"
              value={draft.webPushSubject}
              onChange={(event) =>
                setDraft((current) => ({
                  ...current,
                  webPushSubject: event.target.value,
                }))
              }
            />
          </FormRow>
          <p className="text-xs leading-body text-muted">
            {value.vapidPrivateKey.isConfigured
              ? t("system.notifications.webPush.keysConfigured")
              : t("system.notifications.webPush.keysGeneratedOnSave")}
          </p>
          <div className="flex flex-wrap gap-2">
            <Button
              type="button"
              variant={deviceSubscribed ? "outline" : "solid"}
              disabled={
                webPushBusy ||
                dirty ||
                !value.webPushEnabled ||
                !value.vapidPublicKey ||
                !isWebPushSupported()
              }
              onClick={() =>
                void (deviceSubscribed
                  ? disableCurrentDevice()
                  : enableCurrentDevice())
              }
            >
              <MonitorSmartphone size={15} />
              {deviceSubscribed
                ? t("system.notifications.webPush.disableDevice")
                : t("system.notifications.webPush.enableDevice")}
            </Button>
            {!isWebPushSupported() ? (
              <span className="self-center text-xs text-warning">
                {t("system.notifications.webPush.unsupported")}
              </span>
            ) : null}
          </div>
          {subscriptions?.length ? (
            <ul className="divide-y divide-border-light rounded-lg border border-border-light">
              {subscriptions.map((subscription) => (
                <li
                  key={subscription.id}
                  className="flex items-center justify-between gap-3 px-3 py-2 text-sm"
                >
                  <div className="min-w-0">
                    <p className="truncate text-foreground">
                      {subscription.endpointOrigin}
                    </p>
                    <p className="text-xs text-muted">
                      {subscription.lastError ??
                        t("system.notifications.webPush.subscriptionActive")}
                    </p>
                  </div>
                  <Button
                    type="button"
                    size="sm"
                    variant="ghost"
                    disabled={webPushBusy}
                    aria-label={t(
                      "system.notifications.webPush.revokeSubscription",
                    )}
                    onClick={() => void revokeSubscription(subscription.id)}
                  >
                    <Trash2 size={14} />
                  </Button>
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      </Card>

      <div className="mt-5">
        <Button
          type="button"
          variant="outline"
          disabled={
            testing || dirty || (!value.webhookEnabled && !value.webPushEnabled)
          }
          onClick={() => void test()}
        >
          <Send size={15} />
          {testing
            ? t("system.notifications.webhook.testing")
            : t("system.notifications.webhook.test")}
        </Button>
      </div>

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
                  <span className="ml-2 text-xs text-subtle">
                    {t(
                      `system.notifications.delivery.channel.${delivery.channel}`,
                    )}
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
