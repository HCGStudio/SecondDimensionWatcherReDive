import dayjs from "dayjs";
import React from "react";
import { useTranslation } from "react-i18next";
import { mutate } from "swr";

import {
  ArrowDownNarrowWide,
  CheckCircle2,
  Circle,
  Clock,
  Download,
  Ellipsis,
  FolderOpen,
  Pause,
  Play,
  RefreshCw,
  Trash2,
} from "lucide-react";

import {
  IAnimationInfo,
  SubscriptionAutomationDisposition,
} from "../animation/IAnimationInfo";
import { useAnimationDownloadStatus } from "../animation/hooks";
import {
  cancelDownload,
  pauseDownload,
  reidentifyFilesWithAi,
  resumeDownload,
  retryInference,
  submitDownload,
} from "../animation/utils";
import { useAccess } from "../auth/hooks";
import { retryAfterReauthentication } from "../auth/utils";
import { setPlaybackWatched } from "../playback/api";
import { usePlaybackStates } from "../playback/hooks";
import { formatBytes, formatFileSize } from "../utils/formatBytes";
import { FileBrowser } from "./FileBrowser";
import { useToast } from "./ToastProvider";
import { Button } from "./ui/Button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "./ui/DropdownMenu";
import { Progress } from "./ui/Progress";
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from "./ui/Sheet";

export interface IAnimationInfoProps {
  value: IAnimationInfo;
  showTimeOfDay?: boolean;
}

function formatEpisodeTag(
  season?: number | null,
  episode?: number | null,
): string | null {
  if (season == null && episode == null) return null;
  const s = season != null ? `S${String(season).padStart(2, "0")}` : "";
  const e = episode != null ? `E${String(episode).padStart(2, "0")}` : "";
  return s + e;
}

const colorByState: Record<string, "brand" | "error" | "warning" | "success"> =
  {
    Downloading: "brand",
    Error: "error",
    Paused: "warning",
  };

const DownloadProgress: React.FC<{ id: string }> = ({ id }) => {
  const { data: status } = useAnimationDownloadStatus(id);
  if (!status) return null;

  const color = colorByState[status.state] ?? "success";

  return (
    <div className="mt-2.5 flex items-center gap-3">
      <div className="min-w-0 flex-1">
        <Progress color={color} value={status.progress * 100} max={100} />
      </div>
      <div className="flex shrink-0 items-center gap-2.5 text-xs text-subtle">
        <span className="inline-flex items-center gap-1">
          <ArrowDownNarrowWide size={12} />
          {formatBytes(status.speed)}
        </span>
        <span className="inline-flex items-center gap-1">
          <Clock size={12} />
          {dayjs.duration({ seconds: status.remaining }).humanize()}
        </span>
      </div>
    </div>
  );
};

const automationDispositionClasses: Record<
  SubscriptionAutomationDisposition,
  string
> = {
  Notified: "bg-brand/10 text-brand",
  PendingConfirmation: "bg-warning/10 text-warning",
  AutoDownloadQueued: "bg-success/10 text-success",
  AutoDownloadFailed: "bg-error/10 text-error",
  ManualDownloadQueued: "bg-success/10 text-success",
  DownloadCompleted: "bg-success/10 text-success",
  DownloadCancelled: "bg-warm-silver/15 text-muted",
};

const AutomationDispositionBadge: React.FC<{
  disposition: SubscriptionAutomationDisposition;
}> = ({ disposition }) => {
  const { t } = useTranslation("animation");
  return (
    <span
      className={`shrink-0 rounded-full px-2 py-0.5 text-[11px] font-medium ${automationDispositionClasses[disposition]}`}
    >
      {t(`automation.${disposition}`)}
    </span>
  );
};

const ActionButtons: React.FC<{ value: IAnimationInfo }> = ({ value }) => {
  const { t } = useTranslation(["animation", "settings"]);
  const { canContentWrite, isAdministrator } = useAccess();
  const { data: status } = useAnimationDownloadStatus(
    value.isDownloadTracked && !value.isDownloadFinished ? value.id : null,
  );
  const { addToast } = useToast();
  const [isSheetOpen, setIsSheetOpen] = React.useState(false);
  const [isRetrying, setIsRetrying] = React.useState(false);
  const [isReidentifyingFiles, setIsReidentifyingFiles] = React.useState(false);
  const [isCancelling, setIsCancelling] = React.useState(false);
  const [isUpdatingWatched, setIsUpdatingWatched] = React.useState(false);
  const { data: playbackStates, mutate: mutatePlaybackStates } =
    usePlaybackStates(value.isDownloadFinished ? value.id : undefined);
  const allWatched =
    playbackStates != null &&
    playbackStates.length > 0 &&
    playbackStates.every((state) => state.isWatched);

  const showRetryItem = isAdministrator && value.isAiProcessed;
  const showAiReidentifyItem =
    isAdministrator &&
    value.isDownloadFinished &&
    value.animation != null &&
    value.season != null &&
    value.episode == null;
  const retryLabel = value.animation
    ? t("actions.reinfer")
    : t("actions.inferAi");
  const downloadLabel =
    value.automationDisposition === "PendingConfirmation"
      ? t("actions.confirmDownload")
      : value.automationDisposition === "AutoDownloadFailed" ||
          value.automationDisposition === "DownloadCancelled"
        ? t("actions.retryDownload")
        : t("actions.download");

  const onStartDownload = React.useCallback(async () => {
    try {
      await submitDownload(value.id);
      await mutate(
        (key) =>
          typeof key === "string" && key.startsWith("/api/animationinfo"),
      );
      addToast({ title: t("toast.downloadQueued"), color: "success" });
    } catch {
      addToast({
        title: t("toast.downloadFailed"),
        color: "danger",
        text: t("toast.downloadFailedDesc"),
      });
    }
  }, [value.id, addToast, t]);

  const onRetryInference = React.useCallback(async () => {
    setIsRetrying(true);
    try {
      await retryInference(value.id);
      addToast({ title: t("toast.queued"), color: "success" });
    } catch {
      addToast({ title: t("toast.actionFailed"), color: "danger" });
    } finally {
      setIsRetrying(false);
    }
  }, [value.id, addToast, t]);

  const onReidentifyFilesWithAi = React.useCallback(async () => {
    if (!window.confirm(t("confirm.forceAiReidentifyFiles"))) return;

    setIsReidentifyingFiles(true);
    try {
      await reidentifyFilesWithAi(value.id);
      await mutate(
        (key) =>
          typeof key === "string" &&
          (key.startsWith("/api/file/list?") ||
            key.startsWith("/api/vfs/list?")),
      );
      addToast({ title: t("toast.filesReidentified"), color: "success" });
    } catch {
      addToast({
        title: t("toast.fileReidentifyFailed"),
        color: "danger",
      });
    } finally {
      setIsReidentifyingFiles(false);
    }
  }, [value.id, addToast, t]);

  const onCancelDownload = React.useCallback(
    async (removeFile: boolean) => {
      if (isCancelling) return;

      setIsCancelling(true);
      try {
        const operation = () => cancelDownload(value.id, removeFile);
        if (removeFile) {
          await retryAfterReauthentication(
            operation,
            t("settings:system.reauthenticatePrompt"),
          );
        } else {
          await operation();
        }
      } catch {
        addToast({ title: t("toast.deleteFailed"), color: "danger" });
      } finally {
        setIsCancelling(false);
      }
    },
    [addToast, isCancelling, t, value.id],
  );

  const onDelete = React.useCallback(() => {
    if (window.confirm(t("confirm.deleteFile"))) {
      void onCancelDownload(true);
    }
  }, [onCancelDownload, t]);

  const onToggleAllWatched = React.useCallback(async () => {
    if (!playbackStates || playbackStates.length === 0) return;
    setIsUpdatingWatched(true);
    try {
      await Promise.all(
        playbackStates.map((state) =>
          setPlaybackWatched({
            animationInfoId: value.id,
            path: state.path,
            isWatched: !allWatched,
          }),
        ),
      );
      await mutatePlaybackStates();
      await mutate(
        (key) =>
          typeof key === "string" && key.startsWith("/api/playback/continue?"),
      );
      addToast({
        title: t(allWatched ? "toast.markedUnwatched" : "toast.markedWatched"),
        color: "success",
      });
    } catch {
      addToast({ title: t("toast.watchStateFailed"), color: "danger" });
    } finally {
      setIsUpdatingWatched(false);
    }
  }, [addToast, allWatched, mutatePlaybackStates, playbackStates, t, value.id]);

  const hasOverflowItems =
    showRetryItem ||
    showAiReidentifyItem ||
    (canContentWrite &&
      value.isDownloadTracked &&
      !value.isDownloadFinished &&
      status) ||
    (value.isDownloadTracked &&
      value.isDownloadFinished &&
      !value.isMediaLibraryImport &&
      isAdministrator);

  return (
    <>
      <div className="flex shrink-0 items-center gap-1">
        {/* Primary action: icon-only button */}
        {!value.isDownloadTracked && canContentWrite ? (
          <Button
            size="sm"
            variant="outline"
            className="px-2 py-2"
            title={downloadLabel}
            aria-label={downloadLabel}
            onClick={onStartDownload}
          >
            <Download size={16} />
          </Button>
        ) : null}

        {canContentWrite &&
        value.isDownloadTracked &&
        !value.isDownloadFinished &&
        status ? (
          <>
            {status.state === "Downloading" ? (
              <Button
                size="sm"
                variant="outline"
                className="px-2 py-2"
                title={t("actions.pause")}
                onClick={() =>
                  pauseDownload(value.id).catch(() =>
                    addToast({
                      title: t("toast.pauseFailed"),
                      color: "danger",
                    }),
                  )
                }
              >
                <Pause size={16} />
              </Button>
            ) : null}
            {status.state === "Paused" ? (
              <Button
                size="sm"
                variant="outline"
                className="px-2 py-2"
                title={t("actions.resume")}
                onClick={() =>
                  resumeDownload(value.id).catch(() =>
                    addToast({
                      title: t("toast.resumeFailed"),
                      color: "danger",
                    }),
                  )
                }
              >
                <Play size={16} />
              </Button>
            ) : null}
          </>
        ) : null}

        {value.isDownloadTracked && value.isDownloadFinished ? (
          <>
            {canContentWrite && playbackStates && playbackStates.length > 0 ? (
              <Button
                size="sm"
                variant="outline"
                color={allWatched ? "success" : "default"}
                className="px-2 py-2"
                title={t(
                  allWatched
                    ? "actions.markAllUnwatched"
                    : "actions.markAllWatched",
                )}
                aria-label={t(
                  allWatched
                    ? "actions.markAllUnwatched"
                    : "actions.markAllWatched",
                )}
                disabled={isUpdatingWatched}
                onClick={() => void onToggleAllWatched()}
              >
                {allWatched ? <CheckCircle2 size={16} /> : <Circle size={16} />}
              </Button>
            ) : null}
            <Button
              size="sm"
              variant="outline"
              className="px-2 py-2"
              title={t("actions.browse")}
              disabled={isReidentifyingFiles}
              onClick={() => setIsSheetOpen(true)}
            >
              <FolderOpen size={16} />
            </Button>
          </>
        ) : null}

        {/* Overflow menu: secondary actions */}
        {hasOverflowItems ? (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
                size="sm"
                variant="outline"
                className="px-2 py-2"
                aria-label={t("actions.more")}
              >
                <Ellipsis size={16} />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              {showRetryItem ? (
                <DropdownMenuItem
                  disabled={isRetrying || isReidentifyingFiles}
                  onSelect={onRetryInference}
                >
                  <RefreshCw
                    size={14}
                    className={isRetrying ? "animate-spin" : ""}
                  />
                  {isRetrying ? t("actions.requesting") : retryLabel}
                </DropdownMenuItem>
              ) : null}

              {showAiReidentifyItem ? (
                <DropdownMenuItem
                  disabled={isReidentifyingFiles || isRetrying}
                  onSelect={onReidentifyFilesWithAi}
                >
                  <RefreshCw
                    size={14}
                    className={isReidentifyingFiles ? "animate-spin" : ""}
                  />
                  {isReidentifyingFiles
                    ? t("actions.reidentifyingFiles")
                    : t("actions.forceAiReidentifyFiles")}
                </DropdownMenuItem>
              ) : null}

              {(showRetryItem || showAiReidentifyItem) &&
              ((value.isDownloadTracked && !value.isDownloadFinished) ||
                (value.isDownloadTracked && value.isDownloadFinished)) ? (
                <DropdownMenuSeparator />
              ) : null}

              {value.isDownloadTracked &&
              !value.isDownloadFinished &&
              status &&
              canContentWrite ? (
                <DropdownMenuItem
                  color="danger"
                  disabled={isReidentifyingFiles || isCancelling}
                  onSelect={() => {
                    const removeFile = isAdministrator;
                    if (
                      window.confirm(
                        t(
                          removeFile
                            ? "confirm.cancelAndDelete"
                            : "confirm.cancel",
                        ),
                      )
                    ) {
                      void onCancelDownload(removeFile);
                    }
                  }}
                >
                  <Trash2 size={14} />
                  {t(isAdministrator ? "actions.delete" : "actions.cancel")}
                </DropdownMenuItem>
              ) : null}

              {value.isDownloadTracked &&
              value.isDownloadFinished &&
              !value.isMediaLibraryImport &&
              isAdministrator ? (
                <DropdownMenuItem
                  color="danger"
                  disabled={isReidentifyingFiles || isCancelling}
                  onSelect={onDelete}
                >
                  <Trash2 size={14} />
                  {t("actions.delete")}
                </DropdownMenuItem>
              ) : null}
            </DropdownMenuContent>
          </DropdownMenu>
        ) : null}
      </div>

      <Sheet open={isSheetOpen} onOpenChange={setIsSheetOpen}>
        <SheetContent>
          <SheetHeader>
            <SheetTitle>{value.title}</SheetTitle>
          </SheetHeader>
          <SheetBody>
            <FileBrowser animationId={value.id} />
          </SheetBody>
        </SheetContent>
      </Sheet>
    </>
  );
};

const PlaybackStatus: React.FC<{ animationInfoId: string }> = ({
  animationInfoId,
}) => {
  const { t } = useTranslation("animation");
  const { data: states } = usePlaybackStates(animationInfoId);
  if (!states || states.length === 0) return null;
  const watched = states.filter((state) => state.isWatched).length;
  if (watched === 0) return null;
  const allWatched = watched === states.length;

  return (
    <>
      <span>·</span>
      <span
        className={allWatched ? "text-success" : "text-accent"}
        title={t("watchStatus.summary", { watched, total: states.length })}
      >
        {allWatched
          ? t("watchStatus.watched")
          : t("watchStatus.progress", { watched, total: states.length })}
      </span>
    </>
  );
};

export const AnimationInfo: React.FC<IAnimationInfoProps> = ({
  value,
  showTimeOfDay = false,
}) => {
  const { t, i18n } = useTranslation("animation");
  const tag = formatEpisodeTag(value.season, value.episode);
  const isDownloading = value.isDownloadTracked && !value.isDownloadFinished;
  const publishTime = new Date(value.publishTime);
  const formattedPublishTime = showTimeOfDay
    ? publishTime.toLocaleString(i18n.resolvedLanguage, {
        dateStyle: "medium",
        timeStyle: "short",
      })
    : publishTime.toLocaleDateString(i18n.resolvedLanguage);

  return (
    <div className="border-t border-border py-4 first:border-t-0">
      {/* Row 1: tag + title + actions */}
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            {tag ? (
              <span className="shrink-0 rounded bg-accent/10 px-1.5 py-0.5 font-mono text-xs text-accent">
                {tag}
              </span>
            ) : null}
            <h3 className="font-serif text-base font-medium leading-heading text-foreground break-words">
              {value.title}
            </h3>
            {value.automationDisposition ? (
              <AutomationDispositionBadge
                disposition={value.automationDisposition}
              />
            ) : null}
          </div>

          {/* Row 2: metadata */}
          <div className="mt-1 flex items-center gap-1.5 text-xs leading-body text-subtle">
            {value.group ? (
              <>
                <span>{value.group.name}</span>
                <span>·</span>
              </>
            ) : null}
            <span>{formattedPublishTime}</span>
            {value.releaseSizeBytes != null ? (
              <>
                <span>·</span>
                <span>{formatFileSize(value.releaseSizeBytes)}</span>
              </>
            ) : null}
            {value.isDownloadFinished ? (
              <>
                <span>·</span>
                <span className="text-success">{t("finished")}</span>
                <PlaybackStatus animationInfoId={value.id} />
              </>
            ) : null}
          </div>

          {/* Row 3: description (collapsed to 1 line) */}
          {value.description ? (
            <p className="mt-1 line-clamp-3 text-sm leading-body text-muted">
              {value.description}
            </p>
          ) : null}
        </div>

        <ActionButtons value={value} />
      </div>

      {/* Row 4: download progress (only when downloading) */}
      {isDownloading ? <DownloadProgress id={value.id} /> : null}
    </div>
  );
};
