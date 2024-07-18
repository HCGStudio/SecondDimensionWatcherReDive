import dayjs from "dayjs";
import duration from "dayjs/plugin/duration";
import relativeTime from "dayjs/plugin/relativeTime";

const localeMap: Record<string, (() => Promise<ILocale>) | undefined> = {
  "zh-cn": () => import("dayjs/locale/zh-cn"),
  //TODO: Add more locale
};

const currentLocale = async () => {
  const localeImport = localeMap[navigator.language.toLowerCase()];
  if (localeImport) return await localeImport();
  return "en";
};

export const initDayjs = () => {
  dayjs.extend(duration);
  dayjs.extend(relativeTime);
  currentLocale().then(dayjs.locale);
};
