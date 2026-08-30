import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { IncidentListResponse, IncidentType } from "./types";

export interface IncidentQuery {
  type?: IncidentType | null;
  skip?: number;
  take?: number;
  includeResolved?: boolean;
  enabled?: boolean;
}

export const incidentListKey = ({
  type,
  skip = 0,
  take = 50,
  includeResolved = false,
}: IncidentQuery = {}): string => {
  const params = new URLSearchParams({
    skip: String(skip),
    take: String(take),
    includeResolved: String(includeResolved),
  });
  if (type) params.set("type", type);
  return `/api/incidents?${params.toString()}`;
};

export const useIncidents = (query: IncidentQuery = {}) =>
  useSWR<IncidentListResponse>(
    query.enabled === false ? null : incidentListKey(query),
    fetcher,
    {
      refreshInterval: 15_000,
    },
  );
