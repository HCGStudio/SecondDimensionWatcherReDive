import { IAuthProfile, UserRole } from "../auth/IAuthResult";
import fetcher from "../auth/httpClient";
import { IUserAccount } from "./types";

export const createProfile = (value: { name: string; pin?: string }) =>
  fetcher<IAuthProfile>("/api/accounts/profiles", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(value),
  });

export const renameProfile = (id: string, name: string, signal?: AbortSignal) =>
  fetcher(`/api/accounts/profiles/${id}/name`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ name }),
    signal,
  });

export const setProfilePin = (
  id: string,
  pin: string | null,
  currentPin: string,
  signal?: AbortSignal,
) =>
  fetcher(`/api/accounts/profiles/${id}/pin`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ pin, currentPin: currentPin || null }),
    signal,
  });

export const uploadProfileAvatar = (
  id: string,
  file: File,
  signal?: AbortSignal,
) => {
  const body = new FormData();
  body.append("file", file);
  return fetcher(`/api/accounts/profiles/${id}/avatar`, {
    method: "PUT",
    body,
    signal,
  });
};

export const removeProfileAvatar = (id: string, signal?: AbortSignal) =>
  fetcher(`/api/accounts/profiles/${id}/avatar`, { method: "DELETE", signal });

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
