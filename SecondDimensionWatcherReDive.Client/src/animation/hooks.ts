import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { IAnimationGroupedResponse } from "./IAnimationGrouped";
import { IAnimationInfo } from "./IAnimationInfo";
import { IFileDownloadStatus } from "./IFileDownloadStatus";
import { IResponseArrayData } from "./IResponseArrayData";

export const useAnimationInfo = (skip: number, take: number) =>
  useSWR<IResponseArrayData<IAnimationInfo>>(
    `/api/animationinfo?skip=${skip}&take=${take}`,
    fetcher,
    { refreshInterval: 5000 },
  );

export const useGroupedAnimations = () =>
  useSWR<IAnimationGroupedResponse>("/api/animationinfo/grouped", fetcher, {
    refreshInterval: 5000,
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
