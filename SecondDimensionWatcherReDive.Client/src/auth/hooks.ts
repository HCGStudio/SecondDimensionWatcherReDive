import React from "react";
import useSwr, { mutate } from "swr";

import { IAuthState } from "./IAuthResult";
import { AuthChangeDetail, subscribeToAuthChanges } from "./httpClient";

export const useAllowRegister = () =>
  useSwr<{ allow: boolean }>("/api/auth/allowRegister");
export const useLoginStatus = () => useSwr<IAuthState>("/api/auth/verify");
export const useAccess = () => {
  const { data } = useLoginStatus();
  return {
    isAdministrator: data?.role === "Admin",
    canContentWrite: data?.role === "Admin" || data?.role === "Member",
    canPlaybackWrite: data?.role === "Admin" || data?.role === "Member",
  };
};

type CacheMutator = (
  key: string | ((key: unknown) => boolean),
  data?: unknown,
  options?: { revalidate?: boolean },
) => Promise<unknown>;

export const applyAuthChange = async (
  { auth, profileChanged }: AuthChangeDetail,
  mutateCache: CacheMutator = mutate as CacheMutator,
  redirectToLogin: () => void = () => window.location.assign("/login"),
  reloadForProfileChange: () => void = () => window.location.reload(),
) => {
  const apiKeys = (key: unknown) => {
    const candidate = Array.isArray(key) ? key[0] : key;
    return typeof candidate === "string" && candidate.startsWith("/api/");
  };
  if (!auth || profileChanged) {
    // Remove every profile-scoped response before any component can render
    // under the new identity.
    await mutateCache(apiKeys, undefined, { revalidate: false });
  }

  if (!auth) {
    if (window.location.pathname !== "/login") redirectToLogin();
    return;
  }

  if (profileChanged) {
    // A reload is a security boundary: it unmounts the player/chat and all
    // profile-owned local state before the replacement identity can render.
    reloadForProfileChange();
  } else {
    await mutateCache("/api/auth/verify");
  }
};

export const useAuthSynchronization = () => {
  React.useEffect(
    () =>
      subscribeToAuthChanges((detail) => {
        void applyAuthChange(detail);
      }),
    [],
  );
};
