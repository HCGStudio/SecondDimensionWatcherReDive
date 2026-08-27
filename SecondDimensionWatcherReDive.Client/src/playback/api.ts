import fetcher from "../auth/httpClient";
import {
  PlaybackContext,
  PlaybackPreferences,
  PlaybackState,
  SavePlaybackProgressRequest,
  SetWatchedRequest,
} from "./types";

export const playbackContextKey = (
  animationInfoId: string,
  path: string,
): string => {
  const params = new URLSearchParams({ animationInfoId, path });
  return `/api/playback/context?${params.toString()}`;
};

export const getPlaybackContext = async (
  animationInfoId: string,
  path: string,
): Promise<PlaybackContext> =>
  await fetcher(playbackContextKey(animationInfoId, path));

export const savePlaybackProgress = async (
  request: SavePlaybackProgressRequest,
  keepalive = false,
): Promise<PlaybackState> =>
  await fetcher("/api/playback/progress", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
    keepalive,
  });

export const setPlaybackWatched = async (
  request: SetWatchedRequest,
): Promise<PlaybackState> =>
  await fetcher("/api/playback/watched", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

export const savePlaybackPreferences = async (
  preferences: PlaybackPreferences,
): Promise<PlaybackPreferences> =>
  await fetcher("/api/playback/preferences", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(preferences),
  });
