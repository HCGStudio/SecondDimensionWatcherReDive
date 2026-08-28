import fetcher from "../auth/httpClient";
import {
  ICreateMediaLibrarySourceRequest,
  IMediaLibrarySource,
  IUpdateMediaLibrarySourceRequest,
} from "./types";

const sourcesUrl = "/api/media-library/sources";

export const createMediaLibrarySource = (
  request: ICreateMediaLibrarySourceRequest,
) =>
  fetcher<IMediaLibrarySource>(sourcesUrl, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

export const updateMediaLibrarySource = (
  id: string,
  request: IUpdateMediaLibrarySourceRequest,
) =>
  fetcher<void>(`${sourcesUrl}/${encodeURIComponent(id)}`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

export const deleteMediaLibrarySource = (id: string) =>
  fetcher<void>(`${sourcesUrl}/${encodeURIComponent(id)}`, {
    method: "DELETE",
  });

export const scanMediaLibrarySource = (id: string) =>
  fetcher<void>(`${sourcesUrl}/${encodeURIComponent(id)}/scan`, {
    method: "POST",
  });
