import type { PlaybackPreferences } from "./types";

/** Match browser language tags and the ISO 639 codes found in media metadata. */
export const normalizeLanguage = (
  value: string | null | undefined,
): string | null => {
  const language = value
    ?.trim()
    .toLowerCase()
    .replaceAll("_", "-")
    .split("-")[0];
  if (!language || language === "off" || language === "und") return null;
  switch (language) {
    case "chi":
    case "zho":
      return "zh";
    case "jpn":
      return "ja";
    case "eng":
      return "en";
    default:
      return language;
  }
};

/** Keep the existing API/export fields while presenting one language preference. */
export const getPreferredLanguage = (
  preferences: Pick<PlaybackPreferences, "subtitleLanguage" | "audioLanguage">,
  interfaceLanguage?: string,
): string | null =>
  normalizeLanguage(preferences.subtitleLanguage) ??
  normalizeLanguage(preferences.audioLanguage) ??
  normalizeLanguage(interfaceLanguage);
