import { IAuthProfile, UserRole } from "../auth/IAuthResult";
import fetcher from "../auth/httpClient";
import { IUserAccount } from "./types";

export const createProfile = (value: {
  name: string;
  avatar?: string;
  pin?: string;
}) =>
  fetcher<IAuthProfile>("/api/accounts/profiles", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(value),
  });

export const updateProfile = (
  id: string,
  value: {
    name: string;
    avatar?: string;
    pin?: string;
    currentPin?: string;
    replacePin: boolean;
  },
) =>
  fetcher(`/api/accounts/profiles/${id}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(value),
  });

export const revokeSession = (id: string, asAdministrator = false) =>
  fetcher(`/api/accounts/sessions/${id}${asAdministrator ? "/admin" : ""}`, {
    method: "DELETE",
  });

export const createUser = (value: {
  username: string;
  password: string;
  role: UserRole;
  profileName: string;
}) =>
  fetcher<IUserAccount>("/api/accounts/users", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(value),
  });

export const updateUserAccess = (
  id: string,
  role: UserRole,
  isDisabled: boolean,
) =>
  fetcher(`/api/accounts/users/${id}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ role, isDisabled }),
  });
