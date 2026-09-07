import useSWR from "swr";

import { IAuthProfile } from "../auth/IAuthResult";
import fetcher from "../auth/httpClient";
import { IAccountSession, IUserAccount } from "./types";

export const useProfiles = () =>
  useSWR<IAuthProfile[]>("/api/accounts/profiles", fetcher);

export const useSessions = () =>
  useSWR<IAccountSession[]>("/api/accounts/sessions", fetcher);

export const useUsers = (isAdministrator: boolean) =>
  useSWR<IUserAccount[]>(
    isAdministrator ? "/api/accounts/users" : null,
    fetcher,
  );

export const useAllSessions = (isAdministrator: boolean) =>
  useSWR<IAccountSession[]>(
    isAdministrator ? "/api/accounts/sessions/all" : null,
    fetcher,
  );
