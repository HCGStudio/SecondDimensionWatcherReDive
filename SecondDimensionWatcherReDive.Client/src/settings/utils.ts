import fetcher from "../auth/httpClient";
import { ICreateWebDavTokenResponse } from "./IWebDavToken";

export const createWebDavToken = (
  username?: string,
  description?: string,
  virtualRoot = "/",
  expiresAt?: string,
  userId?: string,
) =>
  fetcher<ICreateWebDavTokenResponse>("/api/webdav-tokens", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      username: username || null,
      description: description || null,
      virtualRoot,
      expiresAt: expiresAt || null,
      userId: userId || null,
    }),
  });

export const deleteWebDavToken = (id: string) =>
  fetcher(`/api/webdav-tokens/${id}`, { method: "DELETE" });
