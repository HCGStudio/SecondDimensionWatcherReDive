import { getBrandIconUrl } from "./brandIcon";
import {
  ColorScheme,
  ThemeId,
  ThemeMode,
  getThemeColors,
  isThemeId,
} from "./themes";

export const THEME_STORAGE_KEY = "sdw.theme";

export type ThemePreferences = {
  mode: ThemeMode;
  lightTheme: ThemeId;
  darkTheme: ThemeId;
};

export const defaultThemePreferences: ThemePreferences = {
  mode: "system",
  lightTheme: "forest",
  darkTheme: "forest",
};

export function parseThemePreferences(value: string | null): ThemePreferences {
  try {
    const saved: unknown = value ? JSON.parse(value) : null;
    if (!saved || typeof saved !== "object" || Array.isArray(saved)) {
      return { ...defaultThemePreferences };
    }
    const preferences = saved as Record<string, unknown>;
    return {
      mode:
        preferences.mode === "light" ||
        preferences.mode === "dark" ||
        preferences.mode === "system"
          ? preferences.mode
          : defaultThemePreferences.mode,
      lightTheme: isThemeId(preferences.lightTheme)
        ? preferences.lightTheme
        : defaultThemePreferences.lightTheme,
      darkTheme: isThemeId(preferences.darkTheme)
        ? preferences.darkTheme
        : defaultThemePreferences.darkTheme,
    };
  } catch {
    return { ...defaultThemePreferences };
  }
}

export function readThemePreferences(): {
  preferences: ThemePreferences;
  storageAvailable: boolean;
} {
  try {
    return {
      preferences: parseThemePreferences(
        window.localStorage.getItem(THEME_STORAGE_KEY),
      ),
      storageAvailable: true,
    };
  } catch {
    return {
      preferences: { ...defaultThemePreferences },
      storageAvailable: false,
    };
  }
}

export function saveThemePreferences(preferences: ThemePreferences): boolean {
  try {
    window.localStorage.setItem(THEME_STORAGE_KEY, JSON.stringify(preferences));
    return true;
  } catch {
    return false;
  }
}

export function getSystemColorScheme(): ColorScheme {
  return typeof window.matchMedia === "function" &&
    window.matchMedia("(prefers-color-scheme: dark)").matches
    ? "dark"
    : "light";
}

export function applyTheme(
  preferences: ThemePreferences,
  systemScheme: ColorScheme,
): void {
  const scheme =
    preferences.mode === "system" ? systemScheme : preferences.mode;
  const id = scheme === "dark" ? preferences.darkTheme : preferences.lightTheme;
  const colors = getThemeColors(id, scheme);
  const root = document.documentElement;
  for (const [token, color] of Object.entries(colors)) {
    root.style.setProperty(`--theme-${token}`, color);
  }
  root.dataset.theme = id;
  root.dataset.colorScheme = scheme;
  root.style.colorScheme = scheme;
  // Cover the document before the application stylesheet has finished loading.
  root.style.backgroundColor = colors.canvas;
  root.style.color = colors.foreground;
  document
    .querySelector('meta[name="theme-color"]')
    ?.setAttribute("content", colors.canvas);
  document
    .querySelector('link[rel="icon"]')
    ?.setAttribute("href", getBrandIconUrl(id, scheme));
}
