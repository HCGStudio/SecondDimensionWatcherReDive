import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { IWebDavToken } from "./IWebDavToken";
import { systemSettingsUrl } from "./systemApi";
import { SystemSettings } from "./systemTypes";

export const useWebDavTokens = () =>
  useSWR<IWebDavToken[]>("/api/webdav-tokens", fetcher);

export const useSystemSettings = () =>
  useSWR<SystemSettings>(systemSettingsUrl, fetcher);
