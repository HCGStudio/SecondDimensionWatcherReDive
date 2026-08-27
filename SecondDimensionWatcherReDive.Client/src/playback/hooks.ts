import useSWR from "swr";

import fetcher from "../auth/httpClient";
import { playbackContextKey } from "./api";
import {
  ContinueWatchingItem,
  PlaybackContext,
  PlaybackPreferences,
  PlaybackState,
} from "./types";

export const useContinueWatching = (limit = 12) =>
  useSWR<ContinueWatchingItem[]>(
    `/api/playback/continue?limit=${limit}`,
    fetcher,
    { refreshInterval: 30_000 },
  );

export const usePlaybackContext = (
  animationInfoId?: string,
  virtualPath?: string,
) =>
  useSWR<PlaybackContext>(
    animationInfoId && virtualPath
      ? playbackContextKey(animationInfoId, virtualPath)
      : null,
    fetcher,
  );

export const usePlaybackStates = (animationInfoId?: string) => {
  const params = animationInfoId
    ? new URLSearchParams({ animationInfoId })
    : null;
  return useSWR<PlaybackState[]>(
    params ? `/api/playback/states?${params.toString()}` : null,
    fetcher,
  );
};

export const usePlaybackPreferences = () =>
  useSWR<PlaybackPreferences>("/api/playback/preferences", fetcher);
