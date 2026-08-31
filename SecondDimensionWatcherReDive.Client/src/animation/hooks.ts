import { useEffect, useRef } from "react";
import useSWR from "swr";
import useSWRInfinite from "swr/infinite";

import fetcher from "../auth/httpClient";
import {
  IAnimationCatalogResponse,
  IAnimationCatalogRevisionResponse,
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

interface ICursorPage {
  nextCursor: string | null;
}

const useCatalogRevision = () =>
  useSWR<IAnimationCatalogRevisionResponse>(
    "/api/animationinfo/catalog-revision",
    fetcher,
    { refreshInterval: 5000 },
  );

const useRevisionBoundPages = <T extends ICursorPage>(
  getUrl: (previousPage?: T) => string | null,
) => {
  const revision = useCatalogRevision();
  const currentRevision = revision.data?.revision;
  const pages = useSWRInfinite<T>(
    (_pageIndex, previousPage) => {
      if (currentRevision === undefined) return null;
      const url = getUrl(previousPage);
      return url === null ? null : `${url}&catalogRevision=${currentRevision}`;
    },
    fetcher,
  );
  const previousRevision = useRef<number | undefined>(undefined);
  useEffect(() => {
    if (currentRevision === undefined) return;
    if (
      previousRevision.current !== undefined &&
      previousRevision.current !== currentRevision
    ) {
      void pages.setSize(1);
    }
    previousRevision.current = currentRevision;
  }, [currentRevision, pages.setSize]);
  return pages;
};

export const useAnimationCatalog = () =>
  useRevisionBoundPages<IAnimationCatalogResponse>((previousPage) =>
    previousPage && !previousPage.nextCursor
      ? null
      : cursorUrl(
          "/api/animationinfo/grouped",
          previousPage?.nextCursor ?? null,
          24,
        ),
  );

export const useUncategorizedAnimations = () =>
  useRevisionBoundPages<IAnimationInfoSummaryResponse>((previousPage) =>
    previousPage && !previousPage.nextCursor
      ? null
      : cursorUrl(
          "/api/animationinfo/uncategorized",
          previousPage?.nextCursor ?? null,
          24,
        ),
  );

export const useAnimationEpisodes = (tmdbId?: string) =>
  useRevisionBoundPages<IAnimationEpisodeResponse>((previousPage) => {
    if (!tmdbId || (previousPage && !previousPage.nextCursor)) return null;
    return cursorUrl(
      `/api/animationinfo/grouped/${encodeURIComponent(tmdbId)}/episodes`,
      previousPage?.nextCursor ?? null,
      50,
    );
  });

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
