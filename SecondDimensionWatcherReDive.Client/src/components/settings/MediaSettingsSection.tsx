import React from "react";
import { useTranslation } from "react-i18next";

import { Braces, KeyRound, Plus, Timer, Trash2 } from "lucide-react";

import {
  MediaLibrarySettings,
  SecretDraft,
  SystemSettings,
  TmdbSettings,
  TmdbSettingsPatch,
  createSecretDraft,
  toSecretMutation,
} from "../../settings/systemTypes";
import { isValidTimeSpan } from "../../settings/timeSpan";
import {
  isAbsoluteServerPath,
  normalizeServerPathForComparison,
} from "../../settings/validation";
import { useToast } from "../ToastProvider";
import { Button } from "../ui/Button";
import { Card } from "../ui/Card";
import { FormRow } from "../ui/FormRow";
import { Input } from "../ui/Input";
import { MediaLibrarySourcesSection } from "./MediaLibrarySourcesSection";
import {
  SecretField,
  SettingsSaveBar,
  SettingsSectionHeader,
} from "./SettingsControls";

interface MediaDraft {
  mediaLibrary: MediaLibrarySettings;
  tmdbApiKey: SecretDraft;
}

const createDraft = (mediaLibrary: MediaLibrarySettings): MediaDraft => ({
  mediaLibrary: {
    ...mediaLibrary,
    allowedRoots: [...mediaLibrary.allowedRoots],
  },
  tmdbApiKey: createSecretDraft(),
});

export interface MediaSettingsSectionProps {
  mediaLibrary: MediaLibrarySettings;
  tmdb: TmdbSettings;
  onSave: (patch: {
    mediaLibrary: MediaLibrarySettings;
    tmdb: TmdbSettingsPatch;
  }) => Promise<SystemSettings>;
}

export const MediaSettingsSection: React.FC<MediaSettingsSectionProps> = ({
  mediaLibrary,
  tmdb,
  onSave,
}) => {
  const { t } = useTranslation("settings");
  const { addToast } = useToast();
  const [draft, setDraft] = React.useState(() => createDraft(mediaLibrary));
  const [saving, setSaving] = React.useState(false);
  const [saved, setSaved] = React.useState(false);

  React.useEffect(() => setDraft(createDraft(mediaLibrary)), [mediaLibrary]);

  const dirty =
    JSON.stringify(draft.mediaLibrary) !== JSON.stringify(mediaLibrary) ||
    draft.tmdbApiKey.operation !== "keep" ||
    draft.tmdbApiKey.value.trim().length > 0;
  const normalizedRoots = draft.mediaLibrary.allowedRoots.map(
    normalizeServerPathForComparison,
  );
  const hasDuplicateRoots =
    new Set(normalizedRoots).size !== normalizedRoots.length;
  const invalid =
    draft.mediaLibrary.allowedRoots.some(
      (root) => !isAbsoluteServerPath(root),
    ) ||
    hasDuplicateRoots ||
    !isValidTimeSpan(draft.mediaLibrary.scanInterval, 1) ||
    !isValidTimeSpan(draft.mediaLibrary.settlingPeriod) ||
    !isValidTimeSpan(draft.mediaLibrary.missingGracePeriod);

  const updateLibrary = React.useCallback(
    (update: Partial<MediaLibrarySettings>) =>
      setDraft((current) => ({
        ...current,
        mediaLibrary: { ...current.mediaLibrary, ...update },
      })),
    [],
  );

  const reset = React.useCallback(() => {
    setDraft(createDraft(mediaLibrary));
    setSaved(false);
  }, [mediaLibrary]);

  const save = React.useCallback(async () => {
    if (saving || invalid) {
      if (invalid)
        addToast({
          title: t("system.media.validationFailed"),
          color: "warning",
        });
      return;
    }
    setSaving(true);
    setSaved(false);
    try {
      await onSave({
        mediaLibrary: {
          ...draft.mediaLibrary,
          allowedRoots: draft.mediaLibrary.allowedRoots.map((root) =>
            root.trim(),
          ),
        },
        tmdb: { apiKey: toSecretMutation(draft.tmdbApiKey) },
      });
      setSaved(true);
      addToast({ title: t("system.media.saved"), color: "success" });
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
  }, [addToast, draft, invalid, onSave, saving, t]);

  return (
    <section>
      <SettingsSectionHeader
        eyebrow={t("system.media.eyebrow")}
        title={t("system.media.title")}
        description={t("system.media.description")}
      />

      <Card
        icon={<KeyRound size={18} />}
        title={t("system.media.tmdb.title")}
        description={t("system.media.tmdb.description")}
      >
        <div className="max-w-xl">
          <SecretField
            id="settings-tmdb-api-key"
            label={t("system.media.tmdb.apiKey")}
            state={tmdb.apiKey}
            draft={draft.tmdbApiKey}
            onChange={(tmdbApiKey) =>
              setDraft((current) => ({ ...current, tmdbApiKey }))
            }
          />
        </div>
      </Card>

      <Card
        className="mt-5"
        icon={<Braces size={18} />}
        title={t("system.media.allowedRoots.title")}
        description={t("system.media.allowedRoots.description")}
      >
        <div className="space-y-2">
          {draft.mediaLibrary.allowedRoots.map((root, index) => (
            <div key={index} className="flex items-center gap-2">
              <Input
                aria-label={t("system.media.allowedRoots.itemLabel", {
                  index: index + 1,
                })}
                value={root}
                placeholder="/media/anime"
                isInvalid={
                  !isAbsoluteServerPath(root) ||
                  normalizedRoots.indexOf(normalizedRoots[index]) !== index
                }
                onChange={(event) => {
                  const allowedRoots = [...draft.mediaLibrary.allowedRoots];
                  allowedRoots[index] = event.target.value;
                  updateLibrary({ allowedRoots });
                }}
              />
              <Button
                variant="icon"
                color="danger"
                aria-label={t("system.media.allowedRoots.remove")}
                onClick={() =>
                  updateLibrary({
                    allowedRoots: draft.mediaLibrary.allowedRoots.filter(
                      (_item, itemIndex) => itemIndex !== index,
                    ),
                  })
                }
              >
                <Trash2 size={16} />
              </Button>
            </div>
          ))}
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() =>
              updateLibrary({
                allowedRoots: [...draft.mediaLibrary.allowedRoots, ""],
              })
            }
          >
            <Plus size={14} />
            {t("system.media.allowedRoots.add")}
          </Button>
        </div>
      </Card>

      <Card
        className="mt-5"
        icon={<Timer size={18} />}
        title={t("system.media.timing.title")}
        description={t("system.media.timing.description")}
      >
        <div className="grid gap-5 sm:grid-cols-3">
          <TimeSpanField
            label={t("system.media.timing.scanInterval")}
            value={draft.mediaLibrary.scanInterval}
            minimumSeconds={1}
            onChange={(scanInterval) => updateLibrary({ scanInterval })}
          />
          <TimeSpanField
            label={t("system.media.timing.settlingPeriod")}
            value={draft.mediaLibrary.settlingPeriod}
            onChange={(settlingPeriod) => updateLibrary({ settlingPeriod })}
          />
          <TimeSpanField
            label={t("system.media.timing.missingGracePeriod")}
            value={draft.mediaLibrary.missingGracePeriod}
            onChange={(missingGracePeriod) =>
              updateLibrary({ missingGracePeriod })
            }
          />
        </div>
        <p className="mt-3 text-xs leading-body text-subtle">
          {t("system.media.timing.help")}
        </p>
      </Card>

      <SettingsSaveBar
        dirty={dirty}
        saving={saving}
        saved={saved}
        onReset={reset}
        onSave={() => void save()}
      />

      <MediaLibrarySourcesSection />
    </section>
  );
};

interface TimeSpanFieldProps {
  label: string;
  value: string;
  minimumSeconds?: number;
  onChange: (value: string) => void;
}

const TimeSpanField: React.FC<TimeSpanFieldProps> = ({
  label,
  value,
  minimumSeconds = 0,
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
