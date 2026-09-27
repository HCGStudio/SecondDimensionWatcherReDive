import React from "react";
import { useTranslation } from "react-i18next";

import { Check, Monitor, Moon, Palette, Sun } from "lucide-react";

import { cn } from "../../lib/cn";
import { useTheme } from "../../theme/ThemeProvider";
import {
  ColorScheme,
  ThemeColors,
  ThemeId,
  ThemeMode,
  builtInThemes,
  getThemeColors,
} from "../../theme/themes";
import { Button } from "../ui/Button";
import { Card } from "../ui/Card";
import { Select, SelectItem, SettingsSectionHeader } from "./SettingsControls";

const modeIcons: Record<ThemeMode, React.ReactNode> = {
  light: <Sun size={19} aria-hidden="true" />,
  dark: <Moon size={19} aria-hidden="true" />,
  system: <Monitor size={19} aria-hidden="true" />,
};

const modes: ThemeMode[] = ["light", "dark", "system"];

export const AppearanceSettingsSection: React.FC = () => {
  const { t } = useTranslation("settings");
  const {
    mode,
    lightTheme,
    darkTheme,
    resolvedMode,
    setMode,
    setLightTheme,
    setDarkTheme,
    setThemePair,
    storageAvailable,
  } = useTheme();

  return (
    <section>
      <SettingsSectionHeader
        eyebrow={t("appearance.eyebrow")}
        title={t("appearance.title")}
        description={t("appearance.description")}
      />

      {!storageAvailable ? (
        <p
          role="status"
          className="mb-5 rounded-lg border border-warning/30 bg-warning/10 px-4 py-3 text-sm text-foreground"
        >
          {t("appearance.storageUnavailable")}
        </p>
      ) : null}

      <div className="rounded-lg border border-border bg-surface p-5">
        <fieldset>
          <legend className="text-lg font-medium text-foreground">
            {t("appearance.mode.title")}
          </legend>
          <p
            id="appearance-mode-description"
            className="mt-1 text-sm leading-body text-muted"
          >
            {t("appearance.mode.description")}
          </p>
          <div className="mt-4 grid gap-3 sm:grid-cols-3">
            {modes.map((value) => (
              <label key={value} className="cursor-pointer">
                <input
                  type="radio"
                  name="appearance-mode"
                  value={value}
                  checked={mode === value}
                  onChange={() => setMode(value)}
                  aria-describedby="appearance-mode-description"
                  className="peer sr-only"
                />
                <span
                  className={cn(
                    "flex items-center gap-3 rounded-lg border px-4 py-3 text-sm transition-colors peer-focus-visible:outline-hidden peer-focus-visible:ring-2 peer-focus-visible:ring-focus peer-focus-visible:ring-offset-2 peer-focus-visible:ring-offset-surface",
                    mode === value
                      ? "border-brand bg-tint font-medium text-accent"
                      : "border-border text-muted hover:bg-surface-muted",
                  )}
                >
                  {modeIcons[value]}
                  {t(`appearance.mode.${value}`)}
                  {mode === value ? (
                    <Check size={16} className="ml-auto" aria-hidden="true" />
                  ) : null}
                </span>
              </label>
            ))}
          </div>
        </fieldset>
        <p role="status" className="mt-4 text-xs leading-body text-muted">
          {t("appearance.currentTheme", {
            theme: t(
              `appearance.themes.${resolvedMode === "light" ? lightTheme : darkTheme}`,
            ),
            mode: t(`appearance.mode.${resolvedMode}`),
          })}
        </p>
      </div>

      <div className="mt-5 grid gap-5 sm:grid-cols-2">
        <ThemeSelection
          scheme="light"
          theme={lightTheme}
          onChange={setLightTheme}
        />
        <ThemeSelection
          scheme="dark"
          theme={darkTheme}
          onChange={setDarkTheme}
        />
      </div>

      <Card
        className="mt-5"
        icon={<Palette size={18} aria-hidden="true" />}
        title={t("appearance.collection.title")}
        description={t("appearance.collection.description")}
      >
        <div className="grid gap-4 sm:grid-cols-2 2xl:grid-cols-3">
          {builtInThemes.map((theme) => {
            const selected = lightTheme === theme.id && darkTheme === theme.id;
            const name = t(`appearance.themes.${theme.id}`);
            return (
              <div
                key={theme.id}
                className={cn(
                  "overflow-hidden rounded-lg border",
                  selected ? "border-brand" : "border-border",
                )}
              >
                <div className="grid grid-cols-2" aria-hidden="true">
                  <ThemePreview colors={theme.light} />
                  <ThemePreview colors={theme.dark} />
                </div>
                <div className="border-t border-border p-3">
                  <h4 className="text-sm font-medium text-foreground">
                    {name}
                  </h4>
                  <Button
                    variant="outline"
                    size="sm"
                    className="mt-3 w-full"
                    disabled={selected}
                    aria-label={t(
                      selected
                        ? "appearance.collection.appliedLabel"
                        : "appearance.collection.applyLabel",
                      { theme: name },
                    )}
                    onClick={() => setThemePair(theme.id)}
                  >
                    {selected ? <Check size={14} aria-hidden="true" /> : null}
                    {t(
                      selected
                        ? "appearance.collection.applied"
                        : "appearance.collection.apply",
                    )}
                  </Button>
                </div>
              </div>
            );
          })}
        </div>
      </Card>
    </section>
  );
};

interface ThemeSelectionProps {
  scheme: ColorScheme;
  theme: ThemeId;
  onChange: (theme: ThemeId) => void;
}

const ThemeSelection: React.FC<ThemeSelectionProps> = ({
  scheme,
  theme,
  onChange,
}) => {
  const { t } = useTranslation("settings");
  const id = `appearance-${scheme}-theme`;
  return (
    <div className="rounded-lg border border-border bg-surface p-5">
      <label
        htmlFor={id}
        className="mb-3 flex items-center gap-2 text-sm font-medium text-foreground"
      >
        {modeIcons[scheme]}
        {t(`appearance.selection.${scheme}`)}
      </label>
      <Select
        id={id}
        value={theme}
        onValueChange={(value) => onChange(value as ThemeId)}
      >
        {builtInThemes.map(({ id: themeId }) => (
          <SelectItem key={themeId} value={themeId}>
            {t(`appearance.themes.${themeId}`)}
          </SelectItem>
        ))}
      </Select>
      <div
        className="mt-4 overflow-hidden rounded-md border border-border"
        aria-hidden="true"
      >
        <ThemePreview colors={getThemeColors(theme, scheme)} />
      </div>
    </div>
  );
};

const ThemePreview: React.FC<{ colors: ThemeColors }> = ({ colors }) => (
  <div
    className="flex h-24 gap-3 p-3"
    style={{ backgroundColor: colors.canvas }}
  >
    <div
      className="w-1/5 space-y-2 rounded p-2"
      style={{ backgroundColor: colors.surface }}
    >
      <div
        className="h-2 w-3 rounded-sm"
        style={{ backgroundColor: colors.brand }}
      />
      <div
        className="h-1 w-full rounded"
        style={{ backgroundColor: colors.border }}
      />
      <div
        className="h-1 w-2/3 rounded"
        style={{ backgroundColor: colors.border }}
      />
    </div>
    <div className="min-w-0 flex-1 space-y-2 py-1">
      <div
        className="h-2 w-2/3 rounded"
        style={{ backgroundColor: colors.foreground }}
      />
      <div
        className="h-1 w-full rounded"
        style={{ backgroundColor: colors.border }}
      />
      <div
        className="flex items-center gap-2 rounded p-2"
        style={{ backgroundColor: colors.tint }}
      >
        <div
          className="h-5 w-4 shrink-0 rounded-sm"
          style={{ backgroundColor: colors.brand }}
        />
        <div
          className="h-1 w-1/2 rounded"
          style={{ backgroundColor: colors.muted }}
        />
      </div>
    </div>
  </div>
);
