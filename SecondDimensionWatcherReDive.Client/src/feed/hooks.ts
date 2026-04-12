import useSWR from "swr";
import fetcher from "../auth/httpClient";
import { IFeed } from "./IFeed";

export const useFeeds = () =>
  useSWR<IFeed[]>("/api/feed", fetcher, { refreshInterval: 5000 });
