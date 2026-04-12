import useSWR from "swr";
import fetcher from "../auth/httpClient";
import { IFileStoreListResult } from "./IFileStoreListResult";

export const useFileList = (id: string, relativeDir?: string) => {
  const params = new URLSearchParams({ id });
  if (relativeDir) params.set("relativeDir", relativeDir);
  return useSWR<IFileStoreListResult[]>(
    `/api/file/list?${params.toString()}`,
    fetcher,
  );
};
