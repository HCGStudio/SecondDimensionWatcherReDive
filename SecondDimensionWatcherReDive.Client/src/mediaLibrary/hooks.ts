import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { IMediaLibrarySource } from "./types";

export const useMediaLibrarySources = () =>
  useSWR<IMediaLibrarySource[]>("/api/media-library/sources", fetcher, {
    refreshInterval: 3000,
  });
