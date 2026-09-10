import fetcher from "../auth/httpClient";
import { AiSelection } from "../chat/types";

export const runTask = async (id: string, selection: AiSelection = {}) => {
  return await fetcher(`/api/tasks/${id}/run`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(selection),
  });
};

const mutateJobs = async (action: "retry" | "resolve", ids: string[]) =>
  await fetcher<{ affectedCount: number }>(`/api/jobs/${action}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ ids }),
  });

export const retryJobs = async (ids: string[]) => mutateJobs("retry", ids);

export const resolveJobs = async (ids: string[]) => mutateJobs("resolve", ids);
