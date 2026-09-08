import useSWR from "swr";

import fetcher from "../auth/httpClient";

export interface CapacityEntry {
  itemId: string;
  title: string;
  expectedBytes: number | null;
  state: string;
  paused: boolean;
  reasonCode: string;
}

const capacityStates = new Set([
  "Waiting",
  "Reserved",
  "Submitted",
  "Failed",
  "Unknown",
]);
const capacityReasons = new Set([
  "assessingCapacity",
  "queuedForSubmission",
  "waitingForCapacity",
  "unknownSize",
  "storageUnavailable",
  "downloaderUnavailable",
  "resubmitting",
  "preparingSubmission",
  "submissionUncertain",
  "downloading",
  "reconciling",
  "submissionRejected",
  "unknown",
]);

export const useDownloadCapacity = (enabled = true) =>
  useSWR<CapacityEntry[]>(
    enabled ? "/api/download-capacity" : null,
    async (url: string) => {
      const entries = await fetcher<CapacityEntry[]>(url);
      return entries.map((entry) => ({
        ...entry,
        state: capacityStates.has(entry.state) ? entry.state : "Unknown",
        reasonCode: capacityReasons.has(entry.reasonCode)
          ? entry.reasonCode
          : "unknown",
      }));
    },
    { refreshInterval: 5000 },
  );
