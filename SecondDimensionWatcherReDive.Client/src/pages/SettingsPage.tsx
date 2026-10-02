import React from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";

import { AlertTriangle, RefreshCw, RotateCw } from "lucide-react";

import { useAccess } from "../auth/hooks";
import { retryAfterReauthentication } from "../auth/utils";
import { AccessSettingsSection } from "../components/settings/AccessSettingsSection";
import { AiSettingsSection } from "../components/settings/AiSettingsSection";
import { AppearanceSettingsSection } from "../components/settings/AppearanceSettingsSection";
import { DownloadSettingsSection } from "../components/settings/DownloadSettingsSection";
import { HealthSettingsSection } from "../components/settings/HealthSettingsSection";
import { MediaSettingsSection } from "../components/settings/MediaSettingsSection";
import { NotificationSettingsSection } from "../components/settings/NotificationSettingsSection";
import { PlaybackSettingsSection } from "../components/settings/PlaybackSettingsSection";
import { PluginSettingsSection } from "../components/settings/PluginSettingsSection";
import {
  SettingsNavigation,
  SettingsSectionId,
  settingsSectionIds,
} from "../components/settings/SettingsNavigation";
import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Spinner } from "../components/ui/Spinner";
import { ApiError, apiErrorStatus } from "../errors/apiError";
import "../i18n/settingsResources";
import { useSystemSettings } from "../settings/hooks";
import { updateSystemSettings } from "../settings/systemApi";
import { SystemSettings, SystemSettingsPatch } from "../settings/systemTypes";
import { PageTemplate } from "./PageTemplate";

const isSectionId = (value: string | null): value is SettingsSectionId =>
  settingsSectionIds.includes(value as SettingsSectionId);

type SettingsPatchWithoutRevision = Omit<
  SystemSettingsPatch,
  "expectedRevision"
>;

export const SettingsPage: React.FC = () => {
  const { t } = useTranslation(["settings", "errors"]);
  const { isAdministrator } = useAccess();
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedSection = searchParams.get("section");
  const sections: readonly SettingsSectionId[] = isAdministrator
    ? settingsSectionIds
    : ["appearance", "playback"];
  const activeSection: SettingsSectionId =
    isSectionId(requestedSection) && sections.includes(requestedSection)
      ? requestedSection
      : isAdministrator
        ? "ai"
        : "appearance";
  const isPersonalSection =
    activeSection === "appearance" || activeSection === "playback";
  const { data, error, mutate } = useSystemSettings(
    isAdministrator && !isPersonalSection,
  );

  const selectSection = React.useCallback(
    (section: SettingsSectionId) => {
      const next = new URLSearchParams(searchParams);
      next.set("section", section);
      setSearchParams(next, { replace: true });
      const reduceMotion = window.matchMedia(
        "(prefers-reduced-motion: reduce)",
      ).matches;
      window.scrollTo({ top: 0, behavior: reduceMotion ? "auto" : "smooth" });
    },
    [searchParams, setSearchParams],
  );

  const save = React.useCallback(
    async (patch: SettingsPatchWithoutRevision): Promise<SystemSettings> => {
      if (!data) throw new ApiError("settings_not_loaded", 0);
      try {
        const updated = await retryAfterReauthentication(
          () =>
            updateSystemSettings({
              expectedRevision: data.revision,
              ...patch,
            }),
          t("settings:system.reauthenticatePrompt"),
        );
        await mutate(updated, { revalidate: false });
        return updated;
      } catch (saveError) {
        if (apiErrorStatus(saveError) === 409) {
          // Preserve the conflict signal even if the follow-up reload also
          // fails, so the section can explain why the save was rejected.
          await mutate().catch(() => undefined);
        }
        throw saveError;
      }
    },
    [data, mutate, t],
  );

  return (
    <PageTemplate>
      <header className="mb-8">
        <h1 className="font-sans text-2xl font-medium text-foreground">
          {t(
            isAdministrator
              ? "settings:pageTitle"
              : "settings:personalPageTitle",
          )}
        </h1>
        <p className="mt-2 max-w-3xl text-sm leading-body text-muted">
          {t(
            isPersonalSection
              ? `settings:${activeSection}.pageDescription`
              : "settings:system.pageDescription",
          )}
        </p>
      </header>

      {!isPersonalSection && data?.pendingRestart ? (
        <div className="mb-6 flex items-start gap-3 rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm leading-body text-muted">
          <RotateCw size={17} className="mt-0.5 shrink-0 text-warning" />
          <div>
            <p className="font-medium text-foreground">
              {t("settings:system.pendingRestart.title")}
            </p>
            <p className="mt-0.5">
              {t("settings:system.pendingRestart.description")}
            </p>
          </div>
        </div>
      ) : null}

      <div className="grid gap-8 xl:grid-cols-[12rem_minmax(0,1fr)]">
        <aside>
          <SettingsNavigation
            active={activeSection}
            sections={sections}
            onChange={selectSection}
          />
        </aside>
        <div className="min-w-0">
          {activeSection === "appearance" ? (
            <AppearanceSettingsSection />
          ) : activeSection === "playback" ? (
            <PlaybackSettingsSection />
          ) : error ? (
            <EmptyPrompt
              role="alert"
              icon={<AlertTriangle size={48} />}
              title={<h2>{t("errors:loadFailed")}</h2>}
              body={<p>{t("settings:system.loadFailed")}</p>}
              actions={
                <Button variant="outline" onClick={() => void mutate()}>
                  <RefreshCw size={16} />
                  {t("settings:system.retry")}
                </Button>
              }
            />
          ) : !data ? (
            <div className="flex justify-center py-24">
              <Spinner />
            </div>
          ) : (
            <ActiveSection
              active={activeSection}
              settings={data}
              onSave={save}
            />
          )}
        </div>
      </div>
    </PageTemplate>
  );
};

interface ActiveSectionProps {
  active: SettingsSectionId;
  settings: SystemSettings;
  onSave: (patch: SettingsPatchWithoutRevision) => Promise<SystemSettings>;
}

const ActiveSection: React.FC<ActiveSectionProps> = ({
  active,
  settings,
  onSave,
}) => {
  switch (active) {
    case "downloads":
      return (
        <DownloadSettingsSection value={settings.torrent} onSave={onSave} />
      );
    case "media":
      return (
        <MediaSettingsSection
          mediaLibrary={settings.mediaLibrary}
          tmdb={settings.tmdb}
          onSave={onSave}
        />
      );
    case "health":
      return (
        <HealthSettingsSection value={settings.incidents} onSave={onSave} />
      );
    case "access":
      return <AccessSettingsSection value={settings.nfs} onSave={onSave} />;
    case "plugins":
      return <PluginSettingsSection />;
    case "notifications":
      return (
        <NotificationSettingsSection
          value={settings.notifications}
          onSave={onSave}
        />
      );
    case "ai":
    default:
      return <AiSettingsSection value={settings.ai} onSave={onSave} />;
  }
};
