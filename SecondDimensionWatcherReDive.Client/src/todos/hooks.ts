import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { TodoList } from "./types";

export const useTodos = (options?: {
  includeRead?: boolean;
  includeSnoozed?: boolean;
  skip?: number;
  take?: number;
  focus?: string | null;
}) => {
  const params = new URLSearchParams();
  if (options?.includeRead) params.set("includeRead", "true");
  if (options?.includeSnoozed) params.set("includeSnoozed", "true");
  if (options?.skip) params.set("skip", String(options.skip));
  if (options?.take) params.set("take", String(options.take));
  if (options?.focus) params.set("focus", options.focus);
  const query = params.toString();
  return useSWR<TodoList>(`/api/todos${query ? `?${query}` : ""}`, fetcher, {
    refreshInterval: 15_000,
  });
};
