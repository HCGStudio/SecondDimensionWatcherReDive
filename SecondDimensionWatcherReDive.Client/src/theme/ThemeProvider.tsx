import {
  ReactNode,
  createContext,
  useCallback,
  useContext,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
} from "react";

import {
  THEME_STORAGE_KEY,
  ThemePreferences,
  applyTheme,
  getSystemColorScheme,
  readThemePreferences,
  saveThemePreferences,
} from "./preferences";
import { ColorScheme, ThemeId, ThemeMode } from "./themes";

type ThemeContextValue = ThemePreferences & {
  resolvedMode: ColorScheme;
  storageAvailable: boolean;
  setMode: (mode: ThemeMode) => void;
  setLightTheme: (theme: ThemeId) => void;
  setDarkTheme: (theme: ThemeId) => void;
  setThemePair: (theme: ThemeId) => void;
};

const ThemeContext = createContext<ThemeContextValue | null>(null);

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState(readThemePreferences);
  const preferencesRef = useRef(state.preferences);
  const [systemScheme, setSystemScheme] = useState(getSystemColorScheme);
  const resolvedMode =
    state.preferences.mode === "system" ? systemScheme : state.preferences.mode;

  useLayoutEffect(() => {
    applyTheme(state.preferences, systemScheme);
  }, [state.preferences, systemScheme]);

  useEffect(() => {
    if (typeof window.matchMedia !== "function") return;
    const query = window.matchMedia("(prefers-color-scheme: dark)");
    const update = () => setSystemScheme(query.matches ? "dark" : "light");
    query.addEventListener("change", update);
    update();
    return () => query.removeEventListener("change", update);
  }, []);

  useEffect(() => {
    const sync = (event: StorageEvent) => {
      if (event.key !== THEME_STORAGE_KEY && event.key !== null) return;
      try {
        if (event.storageArea !== window.localStorage) return;
      } catch {
        setState((current) => ({ ...current, storageAvailable: false }));
        return;
      }
      // Read the latest value, since queued events may describe an older write.
      const current = readThemePreferences();
      preferencesRef.current = current.preferences;
      setState(current);
    };
    window.addEventListener("storage", sync);
    return () => window.removeEventListener("storage", sync);
  }, []);

  const updatePreferences = useCallback(
    (changes: Partial<ThemePreferences>) => {
      const preferences = { ...preferencesRef.current, ...changes };
      preferencesRef.current = preferences;
      const storageAvailable = saveThemePreferences(preferences);
      setState({ preferences, storageAvailable });
    },
    [],
  );

  const setMode = useCallback(
    (mode: ThemeMode) => updatePreferences({ mode }),
    [updatePreferences],
  );
  const setLightTheme = useCallback(
    (lightTheme: ThemeId) => updatePreferences({ lightTheme }),
    [updatePreferences],
  );
  const setDarkTheme = useCallback(
    (darkTheme: ThemeId) => updatePreferences({ darkTheme }),
    [updatePreferences],
  );
  const setThemePair = useCallback(
    (theme: ThemeId) =>
      updatePreferences({ lightTheme: theme, darkTheme: theme }),
    [updatePreferences],
  );

  const value = useMemo<ThemeContextValue>(
    () => ({
      ...state.preferences,
      storageAvailable: state.storageAvailable,
      resolvedMode,
      setMode,
      setLightTheme,
      setDarkTheme,
      setThemePair,
    }),
    [state, resolvedMode, setMode, setLightTheme, setDarkTheme, setThemePair],
  );

  return (
    <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
  );
}

export function useTheme(): ThemeContextValue {
  const theme = useContext(ThemeContext);
  if (!theme) throw new Error("useTheme must be used within ThemeProvider");
  return theme;
}
