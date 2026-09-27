export type ThemeId =
  "forest" | "ocean" | "sakura" | "violet" | "sand" | "slate";
export type ThemeMode = "light" | "dark" | "system";
export type ColorScheme = "light" | "dark";

type Palette = {
  canvas: string;
  surface: string;
  tint: string;
  "surface-muted": string;
  brand: string;
  accent: string;
  "on-brand": string;
  foreground: string;
  muted: string;
  border: string;
  "border-light": string;
};

export type ThemeColors = Palette & {
  subtle: string;
  "dark-surface": string;
  "dark-deep": string;
  focus: string;
  error: string;
  success: string;
  warning: string;
  "on-error": string;
  "on-success": string;
  "on-warning": string;
  "warm-silver": string;
  "charcoal-warm": string;
  "dark-warm": string;
  "ring-warm": string;
  "ring-deep": string;
  selection: string;
  "selection-foreground": string;
  shadow: string;
};

function palette(colors: Palette, scheme: ColorScheme): ThemeColors {
  const dark = scheme === "dark";
  return {
    ...colors,
    subtle: colors.muted,
    "dark-surface": dark ? colors.surface : "#26332d",
    // Video backdrops remain dark in either color scheme.
    "dark-deep": "#111b16",
    focus: colors.brand,
    error: dark ? "#ff9eaa" : "#b53333",
    success: dark ? "#86d6ad" : "#256b4d",
    warning: dark ? "#e9c071" : "#8a5900",
    "on-error": dark ? "#44151e" : "#ffffff",
    "on-success": dark ? "#102e20" : "#ffffff",
    "on-warning": dark ? "#382700" : "#ffffff",
    "warm-silver": colors.muted,
    "charcoal-warm": colors.muted,
    "dark-warm": colors.foreground,
    "ring-warm": colors.border,
    "ring-deep": colors.muted,
    selection: colors.brand,
    "selection-foreground": colors["on-brand"],
    shadow: dark ? "#000000" : colors.foreground,
  };
}

export const builtInThemes: ReadonlyArray<{
  id: ThemeId;
  light: ThemeColors;
  dark: ThemeColors;
}> = [
  {
    id: "forest",
    light: palette(
      {
        canvas: "#ffffff",
        surface: "#ffffff",
        tint: "#edf5f0",
        "surface-muted": "#f6f8f7",
        brand: "#28644f",
        accent: "#20533f",
        "on-brand": "#ffffff",
        foreground: "#203b30",
        muted: "#62726a",
        border: "#d6e1d9",
        "border-light": "#e7eee9",
      },
      "light",
    ),
    dark: palette(
      {
        canvas: "#101a15",
        surface: "#17241d",
        tint: "#263d30",
        "surface-muted": "#1d2d24",
        brand: "#91d7ae",
        accent: "#b0e7c6",
        "on-brand": "#102b1c",
        foreground: "#e3eee6",
        muted: "#a2b5a7",
        border: "#3c5143",
        "border-light": "#2b3b30",
      },
      "dark",
    ),
  },
  {
    id: "ocean",
    light: palette(
      {
        canvas: "#f7fbff",
        surface: "#ffffff",
        tint: "#e7f2fa",
        "surface-muted": "#edf4fa",
        brand: "#1e618b",
        accent: "#194e72",
        "on-brand": "#ffffff",
        foreground: "#20394c",
        muted: "#5b7081",
        border: "#ccdfea",
        "border-light": "#e2ecf3",
      },
      "light",
    ),
    dark: palette(
      {
        canvas: "#101923",
        surface: "#172330",
        tint: "#243b50",
        "surface-muted": "#1c2d3d",
        brand: "#8acbf4",
        accent: "#b2ddf9",
        "on-brand": "#102c40",
        foreground: "#e1edf6",
        muted: "#a0b4c6",
        border: "#3a5064",
        "border-light": "#2a3a4b",
      },
      "dark",
    ),
  },
  {
    id: "sakura",
    light: palette(
      {
        canvas: "#fff9fa",
        surface: "#ffffff",
        tint: "#fbeaf0",
        "surface-muted": "#faf0f3",
        brand: "#a53d65",
        accent: "#883051",
        "on-brand": "#ffffff",
        foreground: "#4a2c38",
        muted: "#806370",
        border: "#ecd2dc",
        "border-light": "#f4e5eb",
      },
      "light",
    ),
    dark: palette(
      {
        canvas: "#21151c",
        surface: "#2d1e27",
        tint: "#4a2b3d",
        "surface-muted": "#38232f",
        brand: "#f0a2c1",
        accent: "#f8c2d8",
        "on-brand": "#42172b",
        foreground: "#f3e4eb",
        muted: "#c1a4b2",
        border: "#634052",
        "border-light": "#452e3b",
      },
      "dark",
    ),
  },
  {
    id: "violet",
    light: palette(
      {
        canvas: "#faf8ff",
        surface: "#ffffff",
        tint: "#f0eafb",
        "surface-muted": "#f3eff9",
        brand: "#7045a3",
        accent: "#593583",
        "on-brand": "#ffffff",
        foreground: "#392e4c",
        muted: "#746681",
        border: "#ded3ec",
        "border-light": "#ece5f4",
      },
      "light",
    ),
    dark: palette(
      {
        canvas: "#1a1624",
        surface: "#251f32",
        tint: "#3c2f53",
        "surface-muted": "#2e263e",
        brand: "#c6adf1",
        accent: "#dccaf9",
        "on-brand": "#2e194d",
        foreground: "#ede6f6",
        muted: "#b4a8c6",
        border: "#514261",
        "border-light": "#3b304b",
      },
      "dark",
    ),
  },
  {
    id: "sand",
    light: palette(
      {
        canvas: "#fffbf5",
        surface: "#fffefa",
        tint: "#f7eddb",
        "surface-muted": "#faf3e7",
        brand: "#906024",
        accent: "#744b19",
        "on-brand": "#ffffff",
        foreground: "#463829",
        muted: "#7b6b56",
        border: "#e5d7bf",
        "border-light": "#f0e7d8",
      },
      "light",
    ),
    dark: palette(
      {
        canvas: "#1d1912",
        surface: "#2a241b",
        tint: "#443622",
        "surface-muted": "#342b1f",
        brand: "#e7c185",
        accent: "#f2d7ab",
        "on-brand": "#3c280e",
        foreground: "#f0e8da",
        muted: "#bfb09a",
        border: "#5b4b34",
        "border-light": "#403625",
      },
      "dark",
    ),
  },
  {
    id: "slate",
    light: palette(
      {
        canvas: "#f8fafc",
        surface: "#ffffff",
        tint: "#e9eef4",
        "surface-muted": "#f0f3f7",
        brand: "#4b607e",
        accent: "#3b4c64",
        "on-brand": "#ffffff",
        foreground: "#283445",
        muted: "#657184",
        border: "#d5dce5",
        "border-light": "#e7ebf1",
      },
      "light",
    ),
    dark: palette(
      {
        canvas: "#15191f",
        surface: "#1e242d",
        tint: "#313d4e",
        "surface-muted": "#272f3b",
        brand: "#b2c4e0",
        accent: "#d1def0",
        "on-brand": "#1e2b3f",
        foreground: "#e7ecf3",
        muted: "#a9b4c4",
        border: "#465367",
        "border-light": "#343e4d",
      },
      "dark",
    ),
  },
];

export function isThemeId(value: unknown): value is ThemeId {
  return builtInThemes.some((theme) => theme.id === value);
}

export function getThemeColors(id: ThemeId, scheme: ColorScheme): ThemeColors {
  return (builtInThemes.find((theme) => theme.id === id) ?? builtInThemes[0])[
    scheme
  ];
}
