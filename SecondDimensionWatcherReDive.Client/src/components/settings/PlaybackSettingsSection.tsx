import React from "react";
import { useTranslation } from "react-i18next";
import { useSWRConfig } from "swr";

import { AlertTriangle, Languages, Play, RefreshCw } from "lucide-react";

import { useAccess } from "../../auth/hooks";
import fetcher, {
  canSendProfileMutation,
  getAuthIdentityKey,
  subscribeToAuthChanges,
} from "../../auth/httpClient";
import { savePlaybackPreferences } from "../../playback/api";
import { getPreferredLanguage } from "../../playback/languagePreferences";
import { PlaybackContext, PlaybackPreferences } from "../../playback/types";
import { Button } from "../ui/Button";
import { Card } from "../ui/Card";
import { EmptyPrompt } from "../ui/EmptyPrompt";
import { FormRow } from "../ui/FormRow";
import { Spinner } from "../ui/Spinner";
import {
  Select,
  SelectItem,
  SettingsSaveBar,
  SettingsSectionHeader,
  ToggleField,
} from "./SettingsControls";

interface PlaybackDraft {
  language: string;
  autoPlayNext: boolean;
  autoSkip: boolean;
}

const createDraft = (value: PlaybackPreferences): PlaybackDraft => ({
  language: getPreferredLanguage(value) ?? "auto",
  autoPlayNext: value.autoPlayNext,
  autoSkip: value.autoSkip ?? false,
});

const languages = ["auto", "zh", "ja", "en"];

export const PlaybackSettingsSection: React.FC = () => {
  const { t } = useTranslation(["settings", "errors"]);
  const { canPlaybackWrite } = useAccess();
  const { mutate } = useSWRConfig();
  const [identityKey] = React.useState(getAuthIdentityKey);
  const active = React.useRef(true);
  const [identityChanged, setIdentityChanged] = React.useState(false);
  const [reload, setReload] = React.useState(0);
  const [value, setValue] = React.useState<PlaybackPreferences | null>(null);
  const [draft, setDraft] = React.useState<PlaybackDraft | null>(null);
  const [loadFailed, setLoadFailed] = React.useState(false);
  const [saveFailed, setSaveFailed] = React.useState(false);
  const [saving, setSaving] = React.useState(false);
  const [saved, setSaved] = React.useState(false);

  React.useEffect(() => {
    active.current = true;
    const unsubscribe = subscribeToAuthChanges(({ auth, profileChanged }) => {
      if (!auth || profileChanged || getAuthIdentityKey() !== identityKey) {
        active.current = false;
        setIdentityChanged(true);
        setValue(null);
        setDraft(null);
      }
    });
    return () => {
      active.current = false;
      unsubscribe();
    };
  }, [identityKey]);

  React.useEffect(() => {
    if (identityChanged || !identityKey) return;
    const controller = new AbortController();
    setLoadFailed(false);
    void fetcher<PlaybackPreferences>("/api/playback/preferences", {
      signal: controller.signal,
    })
      .then((preferences) => {
        if (
          controller.signal.aborted ||
          !active.current ||
          getAuthIdentityKey() !== identityKey
        )
          return;
        setValue(preferences);
        setDraft(createDraft(preferences));
      })
      .catch(() => {
        if (
          !controller.signal.aborted &&
          active.current &&
          getAuthIdentityKey() === identityKey
        )
          setLoadFailed(true);
      });
    return () => controller.abort();
  }, [identityChanged, identityKey, reload]);

  const save = async () => {
    if (
      !draft ||
      saving ||
      !active.current ||
      !canSendProfileMutation(identityKey, canPlaybackWrite)
    )
      return;
    setSaving(true);
    setSaved(false);
    setSaveFailed(false);
    try {
      const language = draft.language === "auto" ? null : draft.language;
      const updated = await savePlaybackPreferences({
        subtitleLanguage: language,
        audioLanguage: language,
        subtitleTrackLabel: null,
        audioTrackLabel: null,
        autoPlayNext: draft.autoPlayNext,
        autoSkip: draft.autoSkip,
      });
      if (!canSendProfileMutation(identityKey)) return;
      await mutate<PlaybackContext>(
        (key) =>
          typeof key === "string" && key.startsWith("/api/playback/context?"),
        (current) =>
          canSendProfileMutation(identityKey) && current
            ? { ...current, preferences: updated }
            : current,
        { revalidate: false },
      );
      if (!canSendProfileMutation(identityKey)) return;
      await mutate("/api/playback/preferences", updated, { revalidate: false });
      if (!active.current || !canSendProfileMutation(identityKey)) return;
      setValue(updated);
      setDraft(createDraft(updated));
      setSaved(true);
    } catch {
      if (active.current && canSendProfileMutation(identityKey))
        setSaveFailed(true);
    } finally {
      if (active.current && getAuthIdentityKey() === identityKey)
        setSaving(false);
    }
  };

  if (loadFailed)
    return (
      <EmptyPrompt
        role="alert"
        icon={<AlertTriangle size={48} />}
        title={<h2>{t("errors:loadFailed")}</h2>}
        body={<p>{t("settings:playback.loadFailed")}</p>}
        actions={
          <Button
            variant="outline"
            onClick={() => setReload((next) => next + 1)}
          >
            <RefreshCw size={16} />
            {t("settings:system.retry")}
          </Button>
        }
      />
    );

  if (identityChanged || !value || !draft)
    return (
      <div className="flex justify-center py-24">
        <Spinner />
      </div>
    );

  const initial = createDraft(value);
  const dirty =
    draft.language !== initial.language ||
    draft.autoPlayNext !== initial.autoPlayNext ||
    draft.autoSkip !== initial.autoSkip;
  const disabled = saving || !canPlaybackWrite;
  const updateDraft = (patch: Partial<PlaybackDraft>) => {
    setDraft({ ...draft, ...patch });
    setSaved(false);
    setSaveFailed(false);
  };

  return (
    <section>
      <SettingsSectionHeader
        eyebrow={t("settings:playback.eyebrow")}
        title={t("settings:playback.title")}
        description={t("settings:playback.description")}
      />

      {!canPlaybackWrite ? (
        <p className="mb-5 text-sm leading-body text-muted">
          {t("settings:playback.readOnly")}
        </p>
      ) : null}

      <Card
        icon={<Play size={18} />}
        title={t("settings:playback.behaviorTitle")}
      >
        <div className="space-y-3">
          <ToggleField
            label={t("settings:playback.autoPlayNext")}
            description={t("settings:playback.autoPlayNextHelp")}
            checked={draft.autoPlayNext}
            disabled={disabled}
            onChange={(autoPlayNext) => updateDraft({ autoPlayNext })}
          />
          <ToggleField
            label={t("settings:playback.autoSkip")}
            description={t("settings:playback.autoSkipHelp")}
            checked={draft.autoSkip}
            disabled={disabled}
            onChange={(autoSkip) => updateDraft({ autoSkip })}
          />
        </div>
      </Card>

      <Card
        className="mt-5"
        icon={<Languages size={18} />}
        title={t("settings:playback.languageTitle")}
        description={t("settings:playback.languageHelp")}
      >
        <FormRow
          label={t("settings:playback.preferredLanguage")}
          htmlFor="playback-preferred-language"
        >
          <Select
            id="playback-preferred-language"
            value={draft.language}
            disabled={disabled}
            className="max-w-sm"
            onValueChange={(language) => updateDraft({ language })}
          >
            {languages.map((language) => (
              <SelectItem key={language} value={language}>
                {t(`settings:playback.languages.${language}`)}
              </SelectItem>
            ))}
            {!languages.includes(initial.language) ? (
              <SelectItem value={initial.language}>
                {initial.language}
              </SelectItem>
            ) : null}
          </Select>
        </FormRow>
      </Card>

      {saveFailed ? (
        <p role="alert" className="mt-5 text-sm text-error">
          {t("settings:playback.saveFailed")}
        </p>
      ) : null}

      {canPlaybackWrite ? (
        <SettingsSaveBar
          dirty={dirty}
          saving={saving}
          saved={saved}
          onSave={() => void save()}
          onReset={() => {
            setDraft(createDraft(value));
            setSaved(false);
            setSaveFailed(false);
          }}
        />
      ) : null}
    </section>
  );
};
