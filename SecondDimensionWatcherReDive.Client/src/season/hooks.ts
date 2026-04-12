import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { IBangumiSubgroup, ISeasonResponse } from "./types";

export const useSeasonBangumis = (year?: number, season?: string) => {
  const params = new URLSearchParams();
  if (year != null && season) {
    params.set("year", String(year));
    params.set("season", season);
  }
  const query = params.toString();
  const url = query ? `/api/season?${query}` : "/api/season";
  return useSWR<ISeasonResponse>(url, fetcher);
};

export const useBangumiSubgroups = (mikanId: number | null) =>
  useSWR<IBangumiSubgroup[]>(
    mikanId != null ? `/api/season/${mikanId}/subgroups` : null,
    fetcher,
  );
