import {
  ArrowDownNarrowWide,
  Clock,
  Download,
  Ellipsis,
  FolderOpen,
  Pause,
  Play,
  RefreshCw,
  Trash2,
} from "lucide-react";
import dayjs from "dayjs";
import React from "react";

import { IAnimationInfo } from "../animation/IAnimationInfo";
import { useAnimationDownloadStatus } from "../animation/hooks";
import {
  cancelDownload,
  pauseDownload,
  resumeDownload,
  retryInference,
  submitDownload,
} from "../animation/utils";
import { formatBytes } from "../utils/formatBytes";
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
  SheetContent,
  SheetHeader,
  SheetBody,
  SheetTitle,
} from "./ui/Sheet";

export interface IAnimationInfoProps {
  value: IAnimationInfo;
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

const ActionButtons: React.FC<{ value: IAnimationInfo }> = ({ value }) => {
  const { data: status } = useAnimationDownloadStatus(
    value.isDownloadTracked && !value.isDownloadFinished ? value.id : "",
  );
  const { addToast } = useToast();
  const [isSheetOpen, setIsSheetOpen] = React.useState(false);
  const [isRetrying, setIsRetrying] = React.useState(false);

  const showRetryItem = value.isAiProcessed;
  const retryLabel = value.animation ? "重新推断" : "AI 推断";

  const onRetryInference = React.useCallback(async () => {
    setIsRetrying(true);
    try {
      await retryInference(value.id);
      addToast({ title: "已加入推断队列", color: "success" });
    } catch {
      addToast({ title: "操作失败", color: "danger" });
    } finally {
      setIsRetrying(false);
    }
  }, [value.id, addToast]);

  const onDelete = React.useCallback(() => {
    if (window.confirm("确定要删除已下载的文件吗？")) {
      cancelDownload(value.id, true).catch(() =>
        addToast({ title: "删除失败", color: "danger" }),
      );
    }
  }, [value.id, addToast]);

  const hasOverflowItems =
    showRetryItem ||
    (value.isDownloadTracked && !value.isDownloadFinished && status) ||
    (value.isDownloadTracked && value.isDownloadFinished);

  return (
    <>
      <div className="flex shrink-0 items-center gap-1">
        {/* Primary action: icon-only button */}
        {!value.isDownloadTracked ? (
          <Button
            size="sm"
            variant="outline"
            className="px-2 py-2"
            title="下载"
            onClick={() =>
              submitDownload(value.id).catch(() =>
                addToast({
                  title: "下载失败",
                  color: "danger",
                  text: "无法提交下载任务",
                }),
              )
            }
          >
            <Download size={16} />
          </Button>
        ) : null}

        {value.isDownloadTracked && !value.isDownloadFinished && status ? (
          <>
            {status.state === "Downloading" ? (
              <Button
                size="sm"
                variant="outline"
                className="px-2 py-2"
                title="暂停"
                onClick={() =>
                  pauseDownload(value.id).catch(() =>
                    addToast({ title: "暂停失败", color: "danger" }),
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
                title="恢复"
                onClick={() =>
                  resumeDownload(value.id).catch(() =>
                    addToast({ title: "恢复失败", color: "danger" }),
                  )
                }
              >
                <Play size={16} />
              </Button>
            ) : null}
          </>
        ) : null}

        {value.isDownloadTracked && value.isDownloadFinished ? (
          <Button
            size="sm"
            variant="outline"
            className="px-2 py-2"
            title="浏览文件"
            onClick={() => setIsSheetOpen(true)}
          >
            <FolderOpen size={16} />
          </Button>
        ) : null}

        {/* Overflow menu: secondary actions */}
        {hasOverflowItems ? (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button size="sm" variant="outline" className="px-2 py-2" aria-label="更多操作">
                <Ellipsis size={16} />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              {showRetryItem ? (
                <DropdownMenuItem
                  disabled={isRetrying}
                  onSelect={onRetryInference}
                >
                  <RefreshCw
                    size={14}
                    className={isRetrying ? "animate-spin" : ""}
                  />
                  {isRetrying ? "请求中..." : retryLabel}
                </DropdownMenuItem>
              ) : null}

              {showRetryItem &&
              ((value.isDownloadTracked && !value.isDownloadFinished) ||
                (value.isDownloadTracked && value.isDownloadFinished)) ? (
                <DropdownMenuSeparator />
              ) : null}

              {value.isDownloadTracked && !value.isDownloadFinished && status ? (
                <DropdownMenuItem
                  color="danger"
                  onSelect={() => {
                    if (window.confirm("确定要取消下载并删除文件吗？")) {
                      cancelDownload(value.id, true).catch(() =>
                        addToast({ title: "删除失败", color: "danger" }),
                      );
                    }
                  }}
                >
                  <Trash2 size={14} />
                  删除
                </DropdownMenuItem>
              ) : null}

              {value.isDownloadTracked && value.isDownloadFinished ? (
                <DropdownMenuItem color="danger" onSelect={onDelete}>
                  <Trash2 size={14} />
                  删除
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

export const AnimationInfo: React.FC<IAnimationInfoProps> = ({ value }) => {
  const tag = formatEpisodeTag(value.season, value.episode);
  const isDownloading =
    value.isDownloadTracked && !value.isDownloadFinished;

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
          </div>

          {/* Row 2: metadata */}
          <div className="mt-1 flex items-center gap-1.5 text-xs leading-body text-subtle">
            {value.group ? (
              <>
                <span>{value.group.name}</span>
                <span>·</span>
              </>
            ) : null}
            <span>{new Date(value.publishTime).toLocaleDateString()}</span>
            {value.isDownloadFinished ? (
              <>
                <span>·</span>
                <span className="text-success">已完成</span>
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
