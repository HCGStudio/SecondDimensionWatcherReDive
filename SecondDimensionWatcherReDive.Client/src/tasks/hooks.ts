import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { ITask } from "./types";

export const useTasks = () =>
  useSWR<ITask[]>("/api/tasks", fetcher, { refreshInterval: 3000 });
