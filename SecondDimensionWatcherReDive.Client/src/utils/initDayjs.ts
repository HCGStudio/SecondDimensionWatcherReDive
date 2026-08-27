import dayjs from "dayjs";
import "dayjs/locale/ja";
import "dayjs/locale/zh-cn";
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

export const setDayjsLocale = (lng: string): void => {
  dayjs.locale(dayjsCodeFor(lng));
};
