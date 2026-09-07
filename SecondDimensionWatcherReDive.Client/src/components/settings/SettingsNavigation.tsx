import React from "react";
import { useTranslation } from "react-i18next";

import {
  Activity,
  BellRing,
  Bot,
  Database,
  Download,
  Network,
} from "lucide-react";

import { cn } from "../../lib/cn";
import { Select } from "./SettingsControls";

export const settingsSectionIds = [
  "ai",
  "downloads",
  "media",
  "health",
  "notifications",
  "access",
] as const;

export type SettingsSectionId = (typeof settingsSectionIds)[number];

const sectionIcons: Record<SettingsSectionId, React.ReactNode> = {
  ai: <Bot size={17} />,
  downloads: <Download size={17} />,
  media: <Database size={17} />,
  health: <Activity size={17} />,
  notifications: <BellRing size={17} />,
  access: <Network size={17} />,
};

export interface SettingsNavigationProps {
  active: SettingsSectionId;
  onChange: (section: SettingsSectionId) => void;
}

export const SettingsNavigation: React.FC<SettingsNavigationProps> = ({
  active,
  onChange,
}) => {
  const { t } = useTranslation("settings");
  return (
    <>
      <div className="lg:hidden">
        <label
          htmlFor="settings-section"
          className="mb-1.5 block text-sm font-medium text-foreground"
        >
          {t("system.navigation.label")}
        </label>
        <Select
          id="settings-section"
          value={active}
          onChange={(event) =>
            onChange(event.target.value as SettingsSectionId)
          }
        >
          {settingsSectionIds.map((section) => (
            <option key={section} value={section}>
              {t(`system.navigation.${section}`)}
            </option>
          ))}
        </Select>
      </div>

      <nav
        className="sticky top-20 hidden space-y-1 lg:block"
        aria-label={t("system.navigation.label")}
      >
        {settingsSectionIds.map((section) => (
          <button
            key={section}
            type="button"
            aria-current={section === active ? "page" : undefined}
            onClick={() => onChange(section)}
            className={cn(
              "flex w-full items-center gap-2.5 rounded-lg px-3 py-2.5 text-left text-sm transition-colors focus:outline-hidden focus:ring-2 focus:ring-focus",
              section === active
                ? "bg-surface font-medium text-foreground shadow-ring"
                : "text-muted hover:bg-surface/60 hover:text-foreground",
            )}
          >
            <span className={section === active ? "text-brand" : "text-subtle"}>
              {sectionIcons[section]}
            </span>
            {t(`system.navigation.${section}`)}
          </button>
        ))}
      </nav>
    </>
  );
};
