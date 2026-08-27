export interface PlaybackState {
  animationInfoId: string;
  path: string;
  virtualPath: string;
  positionSeconds: number;
  durationSeconds: number;
  isWatched: boolean;
  updatedAt: string | null;
  watchedAt: string | null;
}

export interface PlaybackPreferences {
  subtitleLanguage: string | null;
  subtitleTrackLabel: string | null;
  audioLanguage: string | null;
  audioTrackLabel: string | null;
  autoPlayNext: boolean;
  updatedAt?: string | null;
}

export interface ExternalSubtitle {
  path: string;
  virtualPath: string;
  language: string | null;
  label: string;
  format: string;
}

export interface PlaybackTarget {
  animationInfoId: string;
  path: string;
  virtualPath: string;
  title: string;
  animationName?: string | null;
  posterPath?: string | null;
  season?: number | null;
  episode?: number | null;
}

export interface PlaybackContext {
  media: PlaybackTarget;
  state: PlaybackState | null;
  preferences: PlaybackPreferences;
  subtitles: ExternalSubtitle[];
  next: PlaybackTarget | null;
}

export interface ContinueWatchingItem {
  media: PlaybackTarget;
  state: PlaybackState;
}

export interface SavePlaybackProgressRequest {
  animationInfoId: string;
  path: string;
  positionSeconds: number;
  durationSeconds: number;
}

export interface SetWatchedRequest {
  animationInfoId: string;
  path: string;
  isWatched: boolean;
}

export const playbackPercent = (
  positionSeconds: number,
  durationSeconds: number,
): number => {
  if (!Number.isFinite(durationSeconds) || durationSeconds <= 0) return 0;
  return Math.min(100, Math.max(0, (positionSeconds / durationSeconds) * 100));
};
