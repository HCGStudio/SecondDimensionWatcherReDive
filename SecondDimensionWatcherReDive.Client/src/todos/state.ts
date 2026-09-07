import type { TodoStateAction } from "./types";

export const getTodoSnoozeAction = (
  snoozedUntil: string | null,
  now = Date.now(),
): Extract<TodoStateAction, "snooze" | "unsnooze"> =>
  snoozedUntil !== null && new Date(snoozedUntil).getTime() > now
    ? "unsnooze"
    : "snooze";
