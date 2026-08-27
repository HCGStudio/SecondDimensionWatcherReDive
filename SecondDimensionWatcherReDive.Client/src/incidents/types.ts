export type IncidentType =
  | "feedFailure"
  | "downloadStalled"
  | "aiFailure"
  | "fileMappingFailure"
  | "diskSpaceLow";

export type IncidentSeverity = "warning" | "error" | "critical";

export interface Incident {
  id: string;
  type: IncidentType;
  severity: IncidentSeverity;
  title: string;
  detail: string;
  sourceId: string | null;
  detectedAt: string;
  updatedAt: string;
  retryCount: number;
  lastRetryAt: string | null;
  lastRetryError: string | null;
  resolvedAt: string | null;
  canRetry: boolean;
}

export interface IncidentListResponse {
  items: Incident[];
  totalCount: number;
  openCount: number;
  countsByType: Partial<Record<IncidentType, number>>;
}

export interface IncidentRetryResult {
  id?: string;
  incidentId?: string;
  success: boolean;
  error?: string | null;
}

export interface RetryAllIncidentsResponse {
  attempted: number;
  succeeded: number;
  failed: number;
  results: IncidentRetryResult[];
}

export const incidentTypes: IncidentType[] = [
  "feedFailure",
  "downloadStalled",
  "aiFailure",
  "fileMappingFailure",
  "diskSpaceLow",
];
