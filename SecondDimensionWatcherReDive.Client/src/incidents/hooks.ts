import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { IncidentListResponse, IncidentType } from "./types";

export interface IncidentQuery {
  type?: IncidentType | null;
  skip?: number;
  take?: number;
  includeResolved?: boolean;
  focus?: string | null;
}

export const incidentListKey = ({
  type,
  skip = 0,
  take = 50,
  includeResolved = false,
  focus,
}: IncidentQuery = {}): string => {
  const params = new URLSearchParams({
    skip: String(skip),
    take: String(take),
    includeResolved: String(includeResolved),
  });
  if (type) params.set("type", type);
  if (focus) params.set("focus", focus);
  return `/api/incidents?${params.toString()}`;
};

export const useIncidents = (query: IncidentQuery = {}) =>
  useSWR<IncidentListResponse>(incidentListKey(query), fetcher, {
    refreshInterval: 15_000,
  });
