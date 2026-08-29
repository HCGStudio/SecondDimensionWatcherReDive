import useSWR from "swr";
import useSWRInfinite from "swr/infinite";

import fetcher from "../auth/httpClient";
import {
  IAnimationCatalogResponse,
  IAnimationEpisodeResponse,
  IAnimationInfoSummaryResponse,
} from "./IAnimationCatalog";
import { IAnimationInfo } from "./IAnimationInfo";
import { IFileDownloadStatus } from "./IFileDownloadStatus";
import { IResponseArrayData } from "./IResponseArrayData";

export const useAnimationInfo = (skip: number, take: number) =>
  useSWR<IResponseArrayData<IAnimationInfo>>(
    `/api/animationinfo?skip=${skip}&take=${take}`,
    fetcher,
    { refreshInterval: 5000 },
  );

const cursorUrl = (path: string, cursor: string | null, take: number) =>
  `${path}?take=${take}${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}`;

export const useAnimationCatalog = () =>
  useSWRInfinite<IAnimationCatalogResponse>(
    (_pageIndex, previousPage) =>
      previousPage && !previousPage.nextCursor
        ? null
        : cursorUrl(
            "/api/animationinfo/grouped",
            previousPage?.nextCursor ?? null,
            24,
          ),
    fetcher,
  );

export const useUncategorizedAnimations = () =>
  useSWRInfinite<IAnimationInfoSummaryResponse>(
    (_pageIndex, previousPage) =>
      previousPage && !previousPage.nextCursor
        ? null
        : cursorUrl(
            "/api/animationinfo/uncategorized",
            previousPage?.nextCursor ?? null,
            24,
          ),
    fetcher,
  );

export const useAnimationEpisodes = (tmdbId?: string) =>
  useSWRInfinite<IAnimationEpisodeResponse>((_pageIndex, previousPage) => {
    if (!tmdbId || (previousPage && !previousPage.nextCursor)) return null;
    return cursorUrl(
      `/api/animationinfo/grouped/${encodeURIComponent(tmdbId)}/episodes`,
      previousPage?.nextCursor ?? null,
      50,
    );
  }, fetcher);

export const useDownloadingAnimations = (skip: number, take: number) =>
  useSWR<IResponseArrayData<IAnimationInfo>>(
    `/api/animationinfo/downloading?skip=${skip}&take=${take}`,
    fetcher,
    { refreshInterval: 1000 },
  );

export const useDownloadedAnimations = (skip: number, take: number) =>
  useSWR<IResponseArrayData<IAnimationInfo>>(
    `/api/animationinfo/downloaded?skip=${skip}&take=${take}`,
    fetcher,
    { refreshInterval: 5000 },
  );

export const useAnimationDownloadStatus = (id?: string | null) =>
  useSWR<IFileDownloadStatus>(
    id ? `/api/animationinfo/status/${id}` : null,
    fetcher,
    {
      refreshInterval: 1000,
    },
  );
