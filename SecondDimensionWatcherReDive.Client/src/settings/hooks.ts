import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { IWebDavToken } from "./IWebDavToken";

export const useWebDavTokens = () =>
  useSWR<IWebDavToken[]>("/api/webdav-tokens", fetcher);
