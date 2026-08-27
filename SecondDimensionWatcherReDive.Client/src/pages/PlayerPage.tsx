import Artplayer from "artplayer";
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
  ExternalSubtitle,
  PlaybackPreferences,
  PlaybackTarget,
} from "../playback/types";
import { PageTemplate } from "./PageTemplate";

const BRAND_TERRACOTTA = "#c96442";
const PROGRESS_SYNC_INTERVAL_SECONDS = 10;
const OFF_TRACK = "__off__";

interface ResolvedSubtitle extends ExternalSubtitle {
  url: string;
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
  if (normalized.startsWith("zh")) return "zh";
  if (normalized.startsWith("ja") || normalized === "jpn") return "ja";
  if (normalized.startsWith("en") || normalized === "eng") return "en";
  return normalized.split("-")[0] || null;
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
  const [playbackUrl, setPlaybackUrl] = React.useState<string | null>(null);
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
    setPlaybackUrl(null);
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
    setLinkLoading(true);

    Promise.all([
      generatePlaybackLink(animationId, playbackContext.media.path),
      Promise.all(
        playbackContext.subtitles.map(async (subtitle) => ({
          ...subtitle,
          url: (await generatePlaybackLink(animationId, subtitle.path)).url,
        })),
      ),
    ])
      .then(([videoLink, subtitleLinks]) => {
        if (cancelled) return;
        setPlaybackUrl(videoLink.url);
        setSubtitles(subtitleLinks);
      })
      .catch(() => {
        if (cancelled) return;
        const message = i18n.t("player:generateLinkFailed");
        setLinkError(message);
        addToast({ title: message, color: "danger" });
      })
      .finally(() => {
        if (!cancelled) setLinkLoading(false);
      });

    return () => {
      cancelled = true;
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
      subtitles.length === 0 ||
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
  }, [i18n.language, i18n.resolvedLanguage, playbackContext, subtitles]);

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
      lang: artplayerLang,
      autoplay: shouldAutoplay,
      fullscreen: true,
      fullscreenWeb: true,
      pip: true,
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

    const onTimeUpdate = () => persistCurrentProgressRef.current(false);
    const onPause = () => persistCurrentProgressRef.current(true);
    const onSeeked = () => persistCurrentProgressRef.current(true);
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
      art.destroy(false);
      if (artRef.current === art) artRef.current = null;
    };
  }, [
    playbackUrl,
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
      return;
    }
    const subtitle = subtitles.find((item) => item.path === selectedSubtitle);
    if (!subtitle) return;
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
  }, [addToast, selectedSubtitle, subtitles, t]);

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

  return (
    <PageTemplate>
      <Button variant="ghost" size="sm" onClick={goBack} className="mb-4">
        <ArrowLeft size={16} />
        {t("back")}
      </Button>

      {loading ? (
        <div className="flex justify-center py-16">
          <Spinner size={32} />
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
                </div>
                <p className="mt-0.5 text-xs text-muted">
                  {t("useExternalHint")}
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
                <ExternalPlayerButtons playbackUrl={playbackUrl} />
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
                    ? t("tracks.externalMatched", { count: subtitles.length })
                    : t("tracks.noExternal")}
                </p>
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
