import i18n from "i18next";
import LanguageDetector from "i18next-browser-languagedetector";
import { initReactI18next } from "react-i18next";

import { resources } from "./resources";

export const supportedLanguages = ["zh-cn", "en", "ja"] as const;
export type SupportedLanguage = (typeof supportedLanguages)[number];

export const languageLabels: Record<SupportedLanguage, string> = {
  "zh-cn": "中文（简体）",
  en: "English",
  ja: "日本語",
};

i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources,
    fallbackLng: {
      zh: ["zh-cn"],
      "zh-tw": ["zh-cn"],
      "zh-hk": ["zh-cn"],
      "zh-hans": ["zh-cn"],
      "zh-hans-cn": ["zh-cn"],
      "zh-hant": ["zh-cn"],
      default: ["zh-cn"],
    },
    supportedLngs: [...supportedLanguages, "zh"],
    nonExplicitSupportedLngs: true,
    lowerCaseLng: true,
    defaultNS: "common",
    interpolation: { escapeValue: false },
    detection: {
      order: ["localStorage", "navigator"],
      lookupLocalStorage: "i18n.lng",
      caches: ["localStorage"],
    },
    react: { useSuspense: false },
  });

export default i18n;
