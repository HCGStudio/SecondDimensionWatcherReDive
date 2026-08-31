import useSWR from "swr";

import fetcher from "../auth/httpClient";
import {
  LibraryIntegritySummary,
  LibrarySearchResult,
  ReleaseUpgradeCandidate,
  ReleaseUpgradeExecutionResult,
} from "./types";

export const useLibrarySearch = (params: URLSearchParams) => {
  const query = params.toString();
  return useSWR<LibrarySearchResult>(`/api/library/search?${query}`, fetcher, {
    keepPreviousData: true,
  });
};

export const useLibraryIntegrity = () =>
  useSWR<LibraryIntegritySummary[]>("/api/library/integrity", fetcher);

export const executeReleaseUpgrade = async (
  candidate: ReleaseUpgradeCandidate,
  dryRun: boolean,
) =>
  await fetcher<ReleaseUpgradeExecutionResult>(
    "/api/library/upgrades/execute",
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        currentReleaseId: candidate.currentReleaseId,
        candidateReleaseId: candidate.candidateReleaseId,
        dryRun,
      }),
    },
  );
