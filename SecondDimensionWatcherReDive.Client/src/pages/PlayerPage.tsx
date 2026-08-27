import Artplayer from "artplayer";
import artplayerProxyMediabunny from "artplayer-proxy-mediabunny";
import {
  CaptionsFileFormat,
  CaptionsRenderer,
  parseResponse,
} from "media-captions";
import React from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useParams, useSearchParams } from "react-router";
import { mutate as mutateCache } from "swr";

import {
  AlertTriangle,
  ArrowLeft,
  CheckCircle2,
  ChevronRight,
  Languages,
  ListMusic,
  RotateCcw,
} from "lucide-react";

import { ExternalPlayerButtons } from "../components/ExternalPlayerButtons";
import { useToast } from "../components/ToastProvider";
import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Spinner } from "../components/ui/Spinner";
import { generatePlaybackLink } from "../file/utils";
import {
  savePlaybackPreferences,
  savePlaybackProgress,
  setPlaybackWatched,
} from "../playback/api";
import { usePlaybackContext } from "../playback/hooks";
import {
  MkvSubtitleDownloadProgress,
  extractMkvSubtitles,
} from "../playback/mkv/subtitles";
import {
  MkvPlaybackProbe,
  canCopyVideoCodecToMp4,
  isAbortError,
  isMkvPath,
  probeMkvPlayback,
} from "../playback/mkv/support";
import {
  MkvTranscodeStage,
  transcodeMkvForBrowser,
} from "../playback/mkv/transcoder";
import {
  ExternalSubtitle,
  PlaybackPreferences,
  PlaybackTarget,
} from "../playback/types";
import { PageTemplate } from "./PageTemplate";

import "media-captions/styles/captions.css";
import "media-captions/styles/regions.css";

const BRAND_TERRACOTTA = "#c96442";
const PROGRESS_SYNC_INTERVAL_SECONDS = 10;
const OFF_TRACK = "__off__";

interface ResolvedSubtitle extends ExternalSubtitle {
  url: string;
  source: "external" | "embedded";
}

type PlaybackMode = "native" | "mkvProxy" | "transcoded";
type MkvPreparationStage =
  "probing" | "extractingSubtitles" | MkvTranscodeStage;

interface MkvPreparationStatus {
  stage: MkvPreparationStage;
  progress?: number;
}

interface BrowserAudioTrack {
  enabled: boolean;
  id?: string;
  label?: string;
  language?: string;
}

interface BrowserAudioTrackList {
  readonly length: number;
  [index: number]: BrowserAudioTrack;
}

interface VideoWithAudioTracks extends HTMLVideoElement {
  readonly audioTracks?: BrowserAudioTrackList;
}

interface AudioTrackOption {
  key: string;
  label: string;
  language: string | null;
  trackIndex: number | null;
}

interface PendingProgressSave {
  request: Parameters<typeof savePlaybackProgress>[0];
  mediaKey: string;
}

interface PendingPreferenceSave {
  preferences: PlaybackPreferences;
  version: number;
}

const preferenceAudioOptions: AudioTrackOption[] = [
  { key: "preference:auto", label: "auto", language: null, trackIndex: null },
  { key: "preference:ja", label: "ja", language: "ja", trackIndex: null },
  { key: "preference:zh", label: "zh", language: "zh", trackIndex: null },
  { key: "preference:en", label: "en", language: "en", trackIndex: null },
];

const normalizeLanguage = (value: string | null | undefined): string | null => {
  if (!value) return null;
  const normalized = value.trim().toLowerCase().replace("_", "-");
  if (
    normalized.startsWith("zh") ||
    normalized === "chi" ||
    normalized === "zho"
  ) {
    return "zh";
  }
  if (normalized.startsWith("ja") || normalized === "jpn") return "ja";
  if (normalized.startsWith("en") || normalized === "eng") return "en";
  return normalized.split("-")[0] || null;
};

const normalizeCaptionFormat = (format: string): CaptionsFileFormat => {
  const normalized = format.trim().toLowerCase();
  if (normalized === "ass" || normalized === "ssa") return normalized;
  if (normalized === "srt" || normalized === "subrip") return "srt";
  return "vtt";
};

const selectPreferredSubtitle = (
  subtitles: ResolvedSubtitle[],
  preferences: PlaybackPreferences,
  interfaceLanguage: string,
): ResolvedSubtitle | null => {
  if (normalizeLanguage(preferences.subtitleLanguage) === "off") return null;

  if (preferences.subtitleTrackLabel) {
    const exact = subtitles.find(
      (subtitle) => subtitle.label === preferences.subtitleTrackLabel,
    );
    if (exact) return exact;
  }

  const preferredLanguage = normalizeLanguage(preferences.subtitleLanguage);
  if (preferredLanguage) {
    const languageMatch = subtitles.find(
      (subtitle) => normalizeLanguage(subtitle.language) === preferredLanguage,
    );
    if (languageMatch) return languageMatch;
  }

  const interfaceMatch = subtitles.find(
    (subtitle) =>
      normalizeLanguage(subtitle.language) ===
      normalizeLanguage(interfaceLanguage),
  );
  return interfaceMatch ?? (subtitles.length === 1 ? subtitles[0] : null);
};

const readAudioTracks = (
  video: VideoWithAudioTracks,
  unknownLabel: string,
): AudioTrackOption[] => {
  const tracks = video.audioTracks;
  if (!tracks || tracks.length === 0) return [];

  return Array.from({ length: tracks.length }, (_, index) => {
    const track = tracks[index];
    const language = normalizeLanguage(track.language);
    const label =
      track.label?.trim() || language || `${unknownLabel} ${index + 1}`;
    return {
      key: `track:${track.id || index}`,
      label,
      language,
      trackIndex: index,
    };
  });
};

const chooseAudioTrack = (
  tracks: AudioTrackOption[],
  preferences: PlaybackPreferences,
): AudioTrackOption | null => {
  if (preferences.audioTrackLabel) {
    const exact = tracks.find(
      (track) => track.label === preferences.audioTrackLabel,
    );
    if (exact) return exact;
  }
  const preferredLanguage = normalizeLanguage(preferences.audioLanguage);
  return (
    tracks.find((track) => track.language === preferredLanguage) ??
    tracks.find((track) => track.trackIndex != null) ??
    tracks[0] ??
    null
  );
};

const navigateToMedia = (
  navigate: ReturnType<typeof useNavigate>,
  media: PlaybackTarget,
  replace = false,
  autoplay = false,
) => {
  const params = new URLSearchParams({ file: media.path });
  if (autoplay) params.set("autoplay", "1");
  navigate(`/play/${media.animationInfoId}?${params.toString()}`, { replace });
};

export const PlayerPage: React.FC = () => {
  const { t, i18n } = useTranslation("player");
  const { animationId } = useParams<{ animationId: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { addToast } = useToast();

  const file = searchParams.get("file") ?? undefined;
  const shouldAutoplay = searchParams.get("autoplay") === "1";
  const {
    data: playbackContext,
    error: contextError,
    isLoading: contextLoading,
    mutate: mutateContext,
  } = usePlaybackContext(animationId, file);

  const fileName =
    playbackContext?.media.path.split("/").pop() ??
    file?.split("/").pop() ??
    t("unknownFile");
  const [externalPlaybackUrl, setExternalPlaybackUrl] = React.useState<
    string | null
  >(null);
  const [playbackUrl, setPlaybackUrl] = React.useState<string | null>(null);
  const [playbackMode, setPlaybackMode] =
    React.useState<PlaybackMode>("native");
  const [mkvProbe, setMkvProbe] = React.useState<MkvPlaybackProbe | null>(null);
  const [mkvStatus, setMkvStatus] = React.useState<MkvPreparationStatus | null>(
    null,
  );
  const [skippedSubtitleCount, setSkippedSubtitleCount] = React.useState(0);
  const [subtitleDiscoveryComplete, setSubtitleDiscoveryComplete] =
    React.useState(false);
  const [linkLoading, setLinkLoading] = React.useState(false);
  const [linkError, setLinkError] = React.useState<string | null>(null);
  const [subtitles, setSubtitles] = React.useState<ResolvedSubtitle[]>([]);
  const [selectedSubtitle, setSelectedSubtitle] = React.useState(OFF_TRACK);
  const [audioTracks, setAudioTracks] = React.useState<AudioTrackOption[]>([]);
  const [selectedAudio, setSelectedAudio] = React.useState("preference:auto");
  const [savingPreferences, setSavingPreferences] = React.useState(false);
  const [savingWatched, setSavingWatched] = React.useState(false);

  const playerContainerRef = React.useRef<HTMLDivElement>(null);
  const artRef = React.useRef<Artplayer | null>(null);
  const captionsRendererRef = React.useRef<CaptionsRenderer | null>(null);
  const contextRef = React.useRef(playbackContext);
  const preferencesRef = React.useRef(playbackContext?.preferences);
  const lastSyncedTimeRef = React.useRef(-1);
  const initialSeekAppliedRef = React.useRef(false);
  const subtitleSelectionInitializedRef = React.useRef(false);
  const audioSelectionInitializedRef = React.useRef(false);
  const pendingProgressRef = React.useRef<PendingProgressSave | null>(null);
  const progressSaveRunningRef = React.useRef(false);
  const pendingPreferenceRef = React.useRef<PendingPreferenceSave | null>(null);
  const preferenceSaveRunningRef = React.useRef(false);
  const preferenceVersionRef = React.useRef(0);
  const activeMediaKey = `${animationId ?? ""}\u0000${file ?? ""}`;
  const activeMediaKeyRef = React.useRef(activeMediaKey);
  activeMediaKeyRef.current = activeMediaKey;

  const subtitleSignature =
    playbackContext?.subtitles
      .map(
        (subtitle) =>
          `${subtitle.path}\u0000${subtitle.virtualPath}\u0000${subtitle.format}`,
      )
      .join("\u0001") ?? "";

  React.useEffect(() => {
    if (playbackContext) {
      contextRef.current = playbackContext;
      if (!preferenceSaveRunningRef.current) {
        preferencesRef.current = playbackContext.preferences;
      }
    }
  }, [playbackContext]);

  React.useEffect(() => {
    setExternalPlaybackUrl(null);
    setPlaybackUrl(null);
    setPlaybackMode("native");
    setMkvProbe(null);
    setMkvStatus(null);
    setSkippedSubtitleCount(0);
    setSubtitleDiscoveryComplete(false);
    setSubtitles([]);
    setSelectedSubtitle(OFF_TRACK);
    setAudioTracks([]);
    setSelectedAudio("preference:auto");
    setLinkError(null);
    lastSyncedTimeRef.current = -1;
    initialSeekAppliedRef.current = false;
    subtitleSelectionInitializedRef.current = false;
    audioSelectionInitializedRef.current = false;
  }, [animationId, file]);

  React.useEffect(() => {
    if (!animationId || !playbackContext) return;
    let cancelled = false;
    let releasePreparedMedia: (() => void) | null = null;
    const controller = new AbortController();
    setLinkLoading(true);
    setLinkError(null);
    setMkvStatus(null);
    setSubtitleDiscoveryComplete(false);

    const preparePlayback = async () => {
      let generatedLinks = false;
      try {
        const [videoLink, subtitleLinks] = await Promise.all([
          generatePlaybackLink(animationId, playbackContext.media.path),
          Promise.all(
            playbackContext.subtitles.map(async (subtitle) => ({
              ...subtitle,
              source: "external" as const,
              url: (await generatePlaybackLink(animationId, subtitle.path)).url,
            })),
          ),
        ]);
        if (cancelled) return;

        generatedLinks = true;
        setExternalPlaybackUrl(videoLink.url);
        setSubtitles(subtitleLinks);

        if (!isMkvPath(playbackContext.media.path)) {
          setPlaybackMode("native");
          setPlaybackUrl(videoLink.url);
          setSubtitleDiscoveryComplete(true);
          return;
        }

        setMkvStatus({ stage: "probing" });
        let probe: MkvPlaybackProbe | null = null;
        try {
          probe = await probeMkvPlayback(videoLink.url, controller.signal);
        } catch (error) {
          if (isAbortError(error)) throw error;
          // A server/probe incompatibility should still get a chance to use
          // the full-file software fallback.
        }
        if (cancelled) return;
        setMkvProbe(probe);

        if (probe?.videoDecodable && probe.audioDecodable) {
          setPlaybackMode("mkvProxy");
          setPlaybackUrl(videoLink.url);
          setLinkLoading(false);
          setMkvStatus({ stage: "extractingSubtitles", progress: 0 });

          try {
            const extracted = await extractMkvSubtitles(videoLink.url, {
              signal: controller.signal,
              onProgress: (progress: MkvSubtitleDownloadProgress) => {
                if (!cancelled) {
                  setMkvStatus({
                    stage: "extractingSubtitles",
                    progress: progress.fraction ?? undefined,
                  });
                }
              },
            });
            if (cancelled) {
              extracted.cleanup();
              return;
            }
            releasePreparedMedia = extracted.cleanup;
            setSkippedSubtitleCount(extracted.skippedTrackCount);
            const embeddedSubtitles: ResolvedSubtitle[] = extracted.tracks.map(
              (track) => ({
                path: `__mkv_subtitle_${track.trackNumber}`,
                virtualPath: `mkv://subtitle/${track.trackNumber}`,
                language: track.language,
                label: track.label,
                format: track.format,
                source: "embedded",
                url: track.url,
              }),
            );
            setSubtitles([...subtitleLinks, ...embeddedSubtitles]);
          } catch (error) {
            if (!isAbortError(error) && !cancelled) {
              addToast({
                title: i18n.t("player:mkv.subtitleExtractionFailed"),
                color: "warning",
              });
            }
          } finally {
            if (!cancelled) {
              setMkvStatus(null);
              setSubtitleDiscoveryComplete(true);
            }
          }
          return;
        }

        const transcoded = await transcodeMkvForBrowser(
          videoLink.url,
          controller.signal,
          (update) => {
            if (!cancelled) setMkvStatus(update);
          },
          {
            copyVideo:
              probe?.videoDecodable === true &&
              canCopyVideoCodecToMp4(probe.videoCodec),
          },
        );
        if (cancelled) {
          transcoded.release();
          return;
        }
        releasePreparedMedia = transcoded.release;
        setSkippedSubtitleCount(transcoded.skippedSubtitleCount);
        setPlaybackMode("transcoded");
        setPlaybackUrl(transcoded.url);
        setSubtitles([
          ...subtitleLinks,
          ...transcoded.subtitles.map((subtitle) => ({
            ...subtitle,
            source: "embedded" as const,
          })),
        ]);
        setSubtitleDiscoveryComplete(true);
        setMkvStatus(null);
      } catch (error) {
        if (cancelled || isAbortError(error)) return;
        const message = i18n.t(
          generatedLinks
            ? "player:mkv.playbackPreparationFailed"
            : "player:generateLinkFailed",
        );
        setLinkError(message);
        addToast({ title: message, color: "danger" });
      } finally {
        if (!cancelled) setLinkLoading(false);
      }
    };

    void preparePlayback();

    return () => {
      cancelled = true;
      controller.abort();
      releasePreparedMedia?.();
    };
  }, [
    animationId,
    playbackContext?.media.path,
    subtitleSignature,
    addToast,
    i18n,
  ]);

  React.useEffect(() => {
    if (
      !playbackContext ||
      subtitleDiscoveryComplete ||
      subtitleSelectionInitializedRef.current
    ) {
      return;
    }
    const externalSubtitles = subtitles.filter(
      (subtitle) => subtitle.source === "external",
    );
    const selected = selectPreferredSubtitle(
      externalSubtitles,
      playbackContext.preferences,
      i18n.resolvedLanguage ?? i18n.language,
    );
    if (selected) setSelectedSubtitle(selected.path);
  }, [
    i18n.language,
    i18n.resolvedLanguage,
    playbackContext,
    subtitleDiscoveryComplete,
    subtitles,
  ]);

  React.useEffect(() => {
    if (
      !playbackContext ||
      !subtitleDiscoveryComplete ||
      subtitleSelectionInitializedRef.current
    ) {
      return;
    }
    const selected = selectPreferredSubtitle(
      subtitles,
      playbackContext.preferences,
      i18n.resolvedLanguage ?? i18n.language,
    );
    setSelectedSubtitle(selected?.path ?? OFF_TRACK);
    subtitleSelectionInitializedRef.current = true;
  }, [
    i18n.language,
    i18n.resolvedLanguage,
    playbackContext,
    subtitleDiscoveryComplete,
    subtitles,
  ]);

  const flushProgressQueue = React.useCallback(async () => {
    if (progressSaveRunningRef.current) return;
    progressSaveRunningRef.current = true;
    try {
      while (pendingProgressRef.current) {
        const pending = pendingProgressRef.current;
        pendingProgressRef.current = null;
        try {
          const state = await savePlaybackProgress(pending.request);
          if (activeMediaKeyRef.current !== pending.mediaKey) continue;
          void mutateContext(
            (current) => (current ? { ...current, state } : current),
            false,
          );
          void mutateCache(
            (key) =>
              typeof key === "string" &&
              (key.startsWith("/api/playback/continue?") ||
                key.startsWith("/api/playback/states?")),
          );
        } catch {
          if (activeMediaKeyRef.current === pending.mediaKey) {
            addToast({
              title: i18n.t("player:progress.saveFailed"),
              color: "warning",
            });
          }
        }
      }
    } finally {
      progressSaveRunningRef.current = false;
    }
  }, [addToast, i18n, mutateContext]);

  const persistCurrentProgress = React.useCallback(
    (force = false, keepalive = false) => {
      const art = artRef.current;
      const context = contextRef.current;
      if (!art || !context) return;

      const positionSeconds = art.currentTime;
      const durationSeconds = art.duration;
      if (
        !Number.isFinite(positionSeconds) ||
        !Number.isFinite(durationSeconds) ||
        durationSeconds <= 0
      ) {
        return;
      }
      if (
        !force &&
        Math.abs(positionSeconds - lastSyncedTimeRef.current) <
          PROGRESS_SYNC_INTERVAL_SECONDS
      ) {
        return;
      }
      lastSyncedTimeRef.current = positionSeconds;

      const request = {
        animationInfoId: context.media.animationInfoId,
        path: context.media.path,
        positionSeconds,
        durationSeconds,
      };
      const mediaKey = `${context.media.animationInfoId}\u0000${context.media.path}`;
      if (keepalive) {
        // Teardown cannot wait behind an ordinary request. Drop any unsent
        // intermediate sample and dispatch the final position with keepalive.
        pendingProgressRef.current = null;
        void savePlaybackProgress(request, true).catch(() => undefined);
        return;
      }

      // Keep at most one unsent sample. Pause/seek events replace older timer
      // samples, while the single in-flight request preserves write order.
      pendingProgressRef.current = { request, mediaKey };
      void flushProgressQueue();
    },
    [flushProgressQueue],
  );
  const persistCurrentProgressRef = React.useRef(persistCurrentProgress);
  persistCurrentProgressRef.current = persistCurrentProgress;

  React.useEffect(() => {
    if (!playbackUrl || !playbackContext || !playerContainerRef.current) return;

    const lng = i18n.resolvedLanguage ?? i18n.language;
    const artplayerLang = lng.toLowerCase().startsWith("zh")
      ? "zh-cn"
      : lng.toLowerCase().startsWith("ja")
        ? "ja"
        : "en";

    const art = new Artplayer({
      container: playerContainerRef.current,
      url: playbackUrl,
      proxy:
        playbackMode === "mkvProxy"
          ? artplayerProxyMediabunny({
              preflightRange: true,
              dropLateFrames: true,
              loadTimeout: 30_000,
            })
          : undefined,
      lang: artplayerLang,
      autoplay: shouldAutoplay,
      fullscreen: true,
      fullscreenWeb: true,
      pip: playbackMode !== "mkvProxy",
      playbackRate: true,
      aspectRatio: true,
      screenshot: true,
      setting: true,
      hotkey: true,
      subtitleOffset: true,
      theme: BRAND_TERRACOTTA,
      volume: 0.8,
      muted: false,
      autoSize: true,
      autoMini: true,
      flip: true,
      miniProgressBar: true,
      lock: true,
      fastForward: true,
      autoPlayback: false,
      autoOrientation: true,
    });

    artRef.current = art;
    let captionsRenderer: CaptionsRenderer | null = null;
    let captionsOverlay: HTMLDivElement | null = null;
    if (playbackMode === "mkvProxy") {
      captionsOverlay = document.createElement("div");
      captionsOverlay.className = "sdw-captions-overlay";
      // media-captions defaults to z-index 1, below Artplayer's canvas (10).
      captionsOverlay.style.zIndex = "20";
      art.template.$player.appendChild(captionsOverlay);
      captionsRenderer = new CaptionsRenderer(captionsOverlay);
      captionsRendererRef.current = captionsRenderer;
    }

    const onLoadedMetadata = () => {
      const context = contextRef.current;
      if (!context) return;

      if (!initialSeekAppliedRef.current) {
        const resumeAt = context.state?.positionSeconds ?? 0;
        const duration = art.duration;
        if (
          !context.state?.isWatched &&
          resumeAt >= 5 &&
          Number.isFinite(duration) &&
          resumeAt < duration - 10
        ) {
          art.currentTime = resumeAt;
          art.notice.show = i18n.t("player:progress.resumed", {
            time: new Date(resumeAt * 1000).toISOString().slice(11, 19),
          });
          lastSyncedTimeRef.current = resumeAt;
        }
        initialSeekAppliedRef.current = true;
      }

      const discoveredTracks = readAudioTracks(
        art.video as VideoWithAudioTracks,
        i18n.t("player:tracks.unknownAudio"),
      );
      setAudioTracks(discoveredTracks);
      if (!audioSelectionInitializedRef.current) {
        const choice = chooseAudioTrack(
          discoveredTracks.length > 0
            ? discoveredTracks
            : preferenceAudioOptions,
          context.preferences,
        );
        if (choice) {
          setSelectedAudio(choice.key);
          if (choice.trackIndex != null) {
            const nativeTracks = (art.video as VideoWithAudioTracks)
              .audioTracks;
            if (nativeTracks) {
              for (let index = 0; index < nativeTracks.length; index += 1) {
                nativeTracks[index].enabled = index === choice.trackIndex;
              }
            }
          }
        }
        audioSelectionInitializedRef.current = true;
      }

      if (shouldAutoplay) {
        void art.play().catch(() => {
          art.notice.show = i18n.t("player:next.autoplayBlocked");
        });
      }
    };

    const onTimeUpdate = () => {
      if (captionsRenderer) captionsRenderer.currentTime = art.currentTime;
      persistCurrentProgressRef.current(false);
    };
    const onPause = () => persistCurrentProgressRef.current(true);
    const onSeeked = () => {
      if (captionsRenderer) captionsRenderer.currentTime = art.currentTime;
      persistCurrentProgressRef.current(true);
    };
    const onEnded = () => {
      persistCurrentProgressRef.current(true);
      const context = contextRef.current;
      if (context?.preferences.autoPlayNext && context.next) {
        navigateToMedia(navigate, context.next, true, true);
      }
    };
    const onVisibilityChange = () => {
      if (document.visibilityState === "hidden") {
        persistCurrentProgressRef.current(true, true);
      }
    };
    const onBeforeUnload = () => persistCurrentProgressRef.current(true, true);

    art.on("video:loadedmetadata", onLoadedMetadata);
    art.on("video:timeupdate", onTimeUpdate);
    art.on("video:pause", onPause);
    art.on("video:seeked", onSeeked);
    art.on("video:ended", onEnded);
    document.addEventListener("visibilitychange", onVisibilityChange);
    window.addEventListener("beforeunload", onBeforeUnload);

    return () => {
      persistCurrentProgressRef.current(true, true);
      document.removeEventListener("visibilitychange", onVisibilityChange);
      window.removeEventListener("beforeunload", onBeforeUnload);
      captionsRenderer?.destroy();
      captionsOverlay?.remove();
      if (captionsRendererRef.current === captionsRenderer) {
        captionsRendererRef.current = null;
      }
      art.destroy(false);
      if (artRef.current === art) artRef.current = null;
    };
  }, [
    playbackUrl,
    playbackMode,
    playbackContext?.media.virtualPath,
    i18n,
    navigate,
    shouldAutoplay,
  ]);

  React.useEffect(() => {
    const art = artRef.current;
    if (!art) return;
    if (selectedSubtitle === OFF_TRACK) {
      art.subtitle.show = false;
      captionsRendererRef.current?.reset();
      return;
    }
    const subtitle = subtitles.find((item) => item.path === selectedSubtitle);
    if (!subtitle) return;

    if (playbackMode === "mkvProxy") {
      art.subtitle.show = false;
      const renderer = captionsRendererRef.current;
      if (!renderer) return;
      renderer.reset();
      const controller = new AbortController();
      void parseResponse(fetch(subtitle.url, { signal: controller.signal }), {
        type: normalizeCaptionFormat(subtitle.format),
        encoding: "utf-8",
      })
        .then((track) => {
          if (
            controller.signal.aborted ||
            captionsRendererRef.current !== renderer
          ) {
            return;
          }
          renderer.changeTrack(track);
          renderer.currentTime = art.currentTime;
        })
        .catch((error: unknown) => {
          if (!isAbortError(error)) {
            addToast({
              title: t("tracks.subtitleLoadFailed"),
              color: "warning",
            });
          }
        });
      return () => controller.abort();
    }

    captionsRendererRef.current?.reset();
    void art.subtitle
      .switch(subtitle.url, {
        name: subtitle.label,
        type:
          subtitle.format.toLowerCase() === "ssa"
            ? "ass"
            : subtitle.format.toLowerCase(),
        encoding: "utf-8",
      })
      .then(() => {
        if (artRef.current === art) art.subtitle.show = true;
      })
      .catch(() => {
        addToast({ title: t("tracks.subtitleLoadFailed"), color: "warning" });
      });
  }, [addToast, playbackMode, selectedSubtitle, subtitles, t]);

  const flushPreferenceQueue = React.useCallback(async () => {
    if (preferenceSaveRunningRef.current) return;
    preferenceSaveRunningRef.current = true;
    setSavingPreferences(true);
    try {
      while (pendingPreferenceRef.current) {
        const pending = pendingPreferenceRef.current;
        pendingPreferenceRef.current = null;
        try {
          const saved = await savePlaybackPreferences(pending.preferences);
          if (preferenceVersionRef.current !== pending.version) continue;
          preferencesRef.current = saved;
          void mutateContext(
            (context) =>
              context ? { ...context, preferences: saved } : context,
            false,
          );
        } catch {
          if (preferenceVersionRef.current === pending.version) {
            addToast({
              title: i18n.t("player:preferences.saveFailed"),
              color: "danger",
            });
            void mutateContext();
          }
        }
      }
    } finally {
      preferenceSaveRunningRef.current = false;
      setSavingPreferences(false);
    }
  }, [addToast, i18n, mutateContext]);

  const updatePreferences = React.useCallback(
    (changes: Partial<PlaybackPreferences>) => {
      const current = preferencesRef.current;
      if (!current) return;
      const next: PlaybackPreferences = { ...current, ...changes };
      preferencesRef.current = next;
      const version = preferenceVersionRef.current + 1;
      preferenceVersionRef.current = version;
      pendingPreferenceRef.current = { preferences: next, version };
      void mutateContext(
        (context) => (context ? { ...context, preferences: next } : context),
        false,
      );
      void flushPreferenceQueue();
    },
    [flushPreferenceQueue, mutateContext],
  );

  const onSubtitleChange = React.useCallback(
    (path: string) => {
      subtitleSelectionInitializedRef.current = true;
      setSelectedSubtitle(path);
      const subtitle = subtitles.find((item) => item.path === path);
      void updatePreferences({
        subtitleLanguage:
          path === OFF_TRACK ? "off" : (subtitle?.language ?? null),
        subtitleTrackLabel: subtitle?.label ?? null,
      });
    },
    [subtitles, updatePreferences],
  );

  const displayAudioTracks =
    audioTracks.length > 0 ? audioTracks : preferenceAudioOptions;

  const onAudioChange = React.useCallback(
    (key: string) => {
      setSelectedAudio(key);
      const choice = displayAudioTracks.find((track) => track.key === key);
      if (!choice) return;

      if (choice.trackIndex != null) {
        const nativeTracks = (artRef.current?.video as VideoWithAudioTracks)
          ?.audioTracks;
        if (nativeTracks) {
          for (let index = 0; index < nativeTracks.length; index += 1) {
            nativeTracks[index].enabled = index === choice.trackIndex;
          }
        }
      }
      void updatePreferences({
        audioLanguage: choice.language,
        audioTrackLabel: choice.trackIndex != null ? choice.label : null,
      });
    },
    [displayAudioTracks, updatePreferences],
  );

  const onToggleWatched = React.useCallback(async () => {
    if (!playbackContext) return;
    const isWatched = !(playbackContext.state?.isWatched ?? false);
    setSavingWatched(true);
    try {
      const state = await setPlaybackWatched({
        animationInfoId: playbackContext.media.animationInfoId,
        path: playbackContext.media.path,
        isWatched,
      });
      await mutateContext(
        (current) => (current ? { ...current, state } : current),
        false,
      );
      await mutateCache(
        (key) =>
          typeof key === "string" &&
          (key.startsWith("/api/playback/continue?") ||
            key.startsWith("/api/playback/states?")),
      );
      addToast({
        title: t(isWatched ? "watched.marked" : "watched.unmarked"),
        color: "success",
      });
    } catch {
      addToast({ title: t("watched.failed"), color: "danger" });
    } finally {
      setSavingWatched(false);
    }
  }, [addToast, mutateContext, playbackContext, t]);

  const goBack = React.useCallback(() => {
    if (window.history.length > 1) navigate(-1);
    else navigate("/downloaded");
  }, [navigate]);

  const error =
    !animationId || !file
      ? t(!animationId ? "missingId" : "missingFile")
      : contextError
        ? t("contextFailed")
        : linkError;
  const loading = contextLoading || linkLoading || (!error && !playbackUrl);
  const preferences = playbackContext?.preferences;
  const mkvProgressPercent =
    mkvStatus?.progress == null
      ? null
      : Math.round(Math.min(1, Math.max(0, mkvStatus.progress)) * 100);
  const mkvStatusLabel = mkvStatus ? t(`mkv.stages.${mkvStatus.stage}`) : null;

  return (
    <PageTemplate>
      <Button variant="ghost" size="sm" onClick={goBack} className="mb-4">
        <ArrowLeft size={16} />
        {t("back")}
      </Button>

      {loading ? (
        <div className="flex flex-col items-center justify-center gap-3 py-16">
          <Spinner size={32} />
          {mkvStatusLabel ? (
            <div className="w-full max-w-sm text-center">
              <p className="text-sm text-muted">
                {mkvStatusLabel}
                {mkvProgressPercent == null ? "" : ` · ${mkvProgressPercent}%`}
              </p>
              {mkvProgressPercent == null ? null : (
                <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-border-light">
                  <div
                    className="h-full rounded-full bg-brand transition-[width]"
                    style={{ width: `${mkvProgressPercent}%` }}
                  />
                </div>
              )}
              {mkvStatus?.stage === "probing" ? null : (
                <p className="mt-2 text-xs text-subtle">
                  {t("mkv.transcodeNotice")}
                </p>
              )}
            </div>
          ) : null}
        </div>
      ) : error ? (
        <EmptyPrompt
          icon={<AlertTriangle size={48} />}
          title={t("playFailed")}
          body={<p>{error}</p>}
          actions={<Button onClick={goBack}>{t("back")}</Button>}
        />
      ) : playbackUrl && playbackContext ? (
        <>
          <div className="overflow-hidden rounded-2xl border border-border bg-dark-deep shadow-whisper">
            <div ref={playerContainerRef} className="aspect-video w-full" />
          </div>

          <section className="mt-4 rounded-xl border border-border bg-surface p-4 shadow-ring">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <p className="truncate text-sm font-medium text-foreground">
                    {fileName}
                  </p>
                  {playbackContext.state?.isWatched ? (
                    <span className="inline-flex items-center gap-1 rounded-full bg-success/10 px-2 py-0.5 text-[11px] font-medium text-success">
                      <CheckCircle2 size={12} />
                      {t("watched.label")}
                    </span>
                  ) : null}
                  {playbackMode !== "native" ? (
                    <span className="inline-flex items-center rounded-full bg-brand/10 px-2 py-0.5 text-[11px] font-medium text-brand">
                      {t(
                        playbackMode === "mkvProxy"
                          ? "mkv.mode.demuxed"
                          : "mkv.mode.transcoded",
                      )}
                    </span>
                  ) : null}
                </div>
                <p className="mt-0.5 text-xs text-muted">
                  {mkvStatusLabel
                    ? `${mkvStatusLabel}${mkvProgressPercent == null ? "" : ` · ${mkvProgressPercent}%`}`
                    : playbackMode === "mkvProxy" && mkvProbe
                      ? t("mkv.codecSummary", {
                          video: mkvProbe.videoCodec,
                          audio: mkvProbe.audioCodec ?? t("mkv.noAudio"),
                        })
                      : t("useExternalHint")}
                </p>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  disabled={savingWatched}
                  onClick={() => void onToggleWatched()}
                >
                  {playbackContext.state?.isWatched ? (
                    <RotateCcw size={15} />
                  ) : (
                    <CheckCircle2 size={15} />
                  )}
                  {t(
                    playbackContext.state?.isWatched
                      ? "watched.markUnwatched"
                      : "watched.markWatched",
                  )}
                </Button>
                {playbackContext.next ? (
                  <Button
                    size="sm"
                    onClick={() =>
                      navigateToMedia(navigate, playbackContext.next!)
                    }
                  >
                    {t("next.play")}
                    <ChevronRight size={15} />
                  </Button>
                ) : null}
                {externalPlaybackUrl ? (
                  <ExternalPlayerButtons playbackUrl={externalPlaybackUrl} />
                ) : null}
              </div>
            </div>
          </section>

          <section className="mt-4 rounded-xl border border-border bg-surface p-4 shadow-ring">
            <div className="mb-4 flex items-center justify-between gap-3">
              <div>
                <h2 className="font-serif text-base font-medium text-foreground">
                  {t("preferences.title")}
                </h2>
                <p className="mt-0.5 text-xs text-muted">
                  {t("preferences.crossDevice")}
                </p>
              </div>
              {savingPreferences ? <Spinner size={16} /> : null}
            </div>
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <label className="block">
                <span className="mb-1.5 flex items-center gap-1.5 text-xs font-medium text-muted">
                  <Languages size={14} />
                  {t("tracks.subtitle")}
                </span>
                <select
                  value={selectedSubtitle}
                  onChange={(event) => onSubtitleChange(event.target.value)}
                  className="w-full rounded-md border border-border bg-canvas px-3 py-2 text-sm text-foreground focus:border-focus focus:outline-hidden focus:ring-2 focus:ring-focus"
                >
                  <option value={OFF_TRACK}>{t("tracks.off")}</option>
                  {subtitles.map((subtitle) => (
                    <option key={subtitle.path} value={subtitle.path}>
                      {subtitle.label}
                      {subtitle.language ? ` · ${subtitle.language}` : ""}
                    </option>
                  ))}
                </select>
                <p className="mt-1 text-xs text-subtle">
                  {subtitles.length > 0
                    ? t("tracks.available", { count: subtitles.length })
                    : subtitleDiscoveryComplete
                      ? t("tracks.none")
                      : t("mkv.stages.extractingSubtitles")}
                </p>
                {skippedSubtitleCount > 0 ? (
                  <p className="mt-1 text-xs text-warning">
                    {t("mkv.bitmapSubtitlesSkipped", {
                      count: skippedSubtitleCount,
                    })}
                  </p>
                ) : null}
              </label>

              <label className="block">
                <span className="mb-1.5 flex items-center gap-1.5 text-xs font-medium text-muted">
                  <ListMusic size={14} />
                  {t("tracks.audio")}
                </span>
                <select
                  value={selectedAudio}
                  onChange={(event) => onAudioChange(event.target.value)}
                  className="w-full rounded-md border border-border bg-canvas px-3 py-2 text-sm text-foreground focus:border-focus focus:outline-hidden focus:ring-2 focus:ring-focus"
                >
                  {displayAudioTracks.map((track) => (
                    <option key={track.key} value={track.key}>
                      {track.trackIndex == null
                        ? t(`tracks.audioLanguages.${track.label}`)
                        : track.label}
                    </option>
                  ))}
                </select>
                <p className="mt-1 text-xs text-subtle">
                  {audioTracks.length > 0
                    ? t("tracks.audioDetected", { count: audioTracks.length })
                    : t("tracks.audioPreferenceOnly")}
                </p>
              </label>
            </div>

            <label className="mt-4 flex cursor-pointer items-start gap-3 border-t border-border-light pt-4">
              <input
                type="checkbox"
                checked={preferences?.autoPlayNext ?? true}
                onChange={(event) =>
                  void updatePreferences({ autoPlayNext: event.target.checked })
                }
                className="mt-0.5 h-4 w-4 accent-brand"
              />
              <span>
                <span className="block text-sm font-medium text-foreground">
                  {t("next.autoPlay")}
                </span>
                <span className="mt-0.5 block text-xs text-muted">
                  {t("next.autoPlayHint")}
                </span>
              </span>
            </label>
          </section>
        </>
      ) : null}
    </PageTemplate>
  );
};
