import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { IDurableJobPage, ITask } from "./types";

export const useTasks = () =>
  useSWR<ITask[]>("/api/tasks", fetcher, { refreshInterval: 3000 });

export const useDeadLetterJobs = () =>
  useSWR<IDurableJobPage>("/api/jobs?status=deadLetter&take=100", fetcher, {
    refreshInterval: 5000,
  });
