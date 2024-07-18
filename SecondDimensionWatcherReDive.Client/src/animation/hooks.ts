import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { IAnimationInfo } from "./IAnimationInfo";
import { IFileDownloadStatus } from "./IFileDownloadStatus";
import { IResponseArrayData } from "./IResponseArrayData";

export const useAnimationInfo = (skip: number, take: number) =>
  useSWR<IResponseArrayData<IAnimationInfo>>(
    `/api/animationinfo?skip=${skip}&take=${take}`,
    fetcher,
    { refreshInterval: 100 },
  );

export const useAnimationDownloadStatus = (id: string) =>
  useSWR<IFileDownloadStatus>(`/api/animationinfo/status/${id}`, fetcher, {
    refreshInterval: 100,
  });
