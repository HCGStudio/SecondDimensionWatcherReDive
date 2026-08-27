import fetcher from "../auth/httpClient";
import { Incident, RetryAllIncidentsResponse } from "./types";

export const retryIncident = async (id: string): Promise<Incident> =>
  await fetcher(`/api/incidents/${encodeURIComponent(id)}/retry`, {
    method: "POST",
  });

export const retryAllIncidents = async (): Promise<RetryAllIncidentsResponse> =>
  await fetcher("/api/incidents/retry-all", { method: "POST" });
