import useSWR, { mutate } from "swr";

import fetcher from "../auth/httpClient";

export const watchlistStatuses = [
  "planned",
  "watching",
  "onHold",
  "dropped",
  "completed",
] as const;
export interface WatchlistItem {
  id: string;
  tmdbId: string | null;
  mikanId: number | null;
  title: string;
  status: string;
  dayOfWeek: number | null;
  updatedAt: string;
  episodes: {
    animationInfoId: string;
    title: string;
    season: number | null;
    episode: number | null;
    publishedAt: string;
    availability: string;
    path: string | null;
    positionSeconds: number;
  }[];
}
export function currentWeek(): Date {
  const date = new Date();
  date.setHours(0, 0, 0, 0);
  date.setDate(date.getDate() - ((date.getDay() + 6) % 7));
  return date;
}
export function useWatchlist(weekStart = currentWeek().toISOString()) {
  return useSWR<WatchlistItem[]>(
    `/api/watchlist?weekStart=${encodeURIComponent(weekStart)}`,
    fetcher,
  );
}
export const refreshWatchlist = () =>
  mutate((key) => typeof key === "string" && key.startsWith("/api/watchlist"));
export async function saveWatchlist(item: {
  id?: string;
  tmdbId?: string | null;
  mikanId?: number | null;
  title: string;
  status: string;
}) {
  await fetcher("/api/watchlist", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(item),
  });
  await refreshWatchlist();
}
