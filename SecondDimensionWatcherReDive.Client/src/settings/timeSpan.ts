const timeSpanPattern =
  /^(?:(\d+)\.)?(\d{2}):([0-5]\d):([0-5]\d)(?:\.(\d{1,7}))?$/;

export const parseTimeSpanSeconds = (value: string): number | null => {
  const match = value.match(timeSpanPattern);
  if (!match) return null;
  const days = Number(match[1] ?? 0);
  const hours = Number(match[2]);
  const minutes = Number(match[3]);
  const seconds = Number(match[4]);
  const fraction = match[5] ? Number(`0.${match[5]}`) : 0;
  if (!Number.isSafeInteger(days) || hours > 23) return null;
  const total = days * 86400 + hours * 3600 + minutes * 60 + seconds + fraction;
  return Number.isFinite(total) && total <= Number.MAX_SAFE_INTEGER
    ? total
    : null;
};

export const isValidTimeSpan = (value: string, minimumSeconds = 0): boolean => {
  const seconds = parseTimeSpanSeconds(value);
  return seconds != null && seconds >= minimumSeconds;
};
