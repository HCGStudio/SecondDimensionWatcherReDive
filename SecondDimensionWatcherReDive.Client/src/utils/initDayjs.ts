import dayjs from "dayjs";
import duration from "dayjs/plugin/duration";
import relativeTime from "dayjs/plugin/relativeTime";

dayjs.extend(duration);
dayjs.extend(relativeTime);

const dayjsCodeFor = (lng: string): string => {
  const lower = lng.toLowerCase();
  if (lower.startsWith("zh")) return "zh-cn";
  if (lower.startsWith("ja")) return "ja";
  return "en";
};

const localeLoaders: Record<string, () => Promise<unknown>> = {
  "zh-cn": () => import("dayjs/locale/zh-cn"),
  ja: () => import("dayjs/locale/ja"),
  en: () => Promise.resolve(),
};

export const setDayjsLocale = async (lng: string): Promise<void> => {
  const code = dayjsCodeFor(lng);
  const loader = localeLoaders[code] ?? localeLoaders["en"]!;
  await loader();
  dayjs.locale(code);
};
