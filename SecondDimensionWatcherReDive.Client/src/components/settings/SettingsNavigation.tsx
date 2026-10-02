import React from "react";
import { useTranslation } from "react-i18next";

import {
  Activity,
  BellRing,
  Bot,
  Database,
  Download,
  Network,
  Palette,
  Play,
  Puzzle,
  ShieldCheck,
} from "lucide-react";

import { cn } from "../../lib/cn";
import { Select, SelectItem } from "./SettingsControls";

export const settingsSectionIds = [
  "appearance",
  "playback",
  "security",
  "ai",
  "downloads",
  "media",
  "health",
  "notifications",
  "access",
  "plugins",
] as const;

export type SettingsSectionId = (typeof settingsSectionIds)[number];

const sectionIcons: Record<SettingsSectionId, React.ReactNode> = {
  appearance: <Palette size={17} />,
  playback: <Play size={17} />,
  security: <ShieldCheck size={17} />,
  ai: <Bot size={17} />,
  downloads: <Download size={17} />,
  media: <Database size={17} />,
  health: <Activity size={17} />,
  notifications: <BellRing size={17} />,
  access: <Network size={17} />,
  plugins: <Puzzle size={17} />,
};

export interface SettingsNavigationProps {
  active: SettingsSectionId;
  sections?: readonly SettingsSectionId[];
  onChange: (section: SettingsSectionId) => void;
}

export const SettingsNavigation: React.FC<SettingsNavigationProps> = ({
  active,
  sections = settingsSectionIds,
  onChange,
}) => {
  const { t } = useTranslation("settings");
  return (
    <>
      <div className="xl:hidden">
        <label
          htmlFor="settings-section"
          className="mb-1.5 block text-sm font-medium text-foreground"
        >
          {t("system.navigation.label")}
        </label>
        <Select
          id="settings-section"
          value={active}
          onValueChange={(value) => onChange(value as SettingsSectionId)}
        >
          {sections.map((section) => (
            <SelectItem key={section} value={section}>
              {t(`system.navigation.${section}`)}
            </SelectItem>
          ))}
        </Select>
      </div>

      <nav
        className="sticky top-24 hidden space-y-1 xl:block"
        aria-label={t("system.navigation.label")}
      >
        {sections.map((section) => (
          <button
            key={section}
            type="button"
            aria-current={section === active ? "page" : undefined}
            onClick={() => onChange(section)}
            className={cn(
              "flex w-full items-center gap-2.5 rounded-lg px-3 py-2.5 text-left text-sm transition-colors focus:outline-hidden focus:ring-2 focus:ring-focus",
              section === active
                ? "bg-tint font-medium text-accent"
                : "text-muted hover:bg-surface-muted hover:text-foreground",
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
