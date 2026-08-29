import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { TodoList } from "./types";

export const useTodos = (options?: {
  includeRead?: boolean;
  includeSnoozed?: boolean;
}) => {
  const params = new URLSearchParams();
  if (options?.includeRead) params.set("includeRead", "true");
  if (options?.includeSnoozed) params.set("includeSnoozed", "true");
  const query = params.toString();
  return useSWR<TodoList>(`/api/todos${query ? `?${query}` : ""}`, fetcher);
};
