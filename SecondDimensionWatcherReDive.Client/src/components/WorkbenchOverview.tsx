import React from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";

import { ArrowRight, Download, Pause, Play, Rss } from "lucide-react";

import { IAnimationInfo } from "../animation/IAnimationInfo";
import {
  useAnimationDownloadStatus,
  useDownloadingAnimations,
} from "../animation/hooks";
import { pauseDownload, resumeDownload } from "../animation/utils";
import { useAccess } from "../auth/hooks";
import { useFeeds } from "../feed/hooks";
import { formatBytes } from "../utils/formatBytes";
import { useToast } from "./ToastProvider";
import { Spinner } from "./ui/Spinner";

const DownloadSummary: React.FC<{ item: IAnimationInfo }> = ({ item }) => {
  const { t } = useTranslation("animation");
  const { data: status, error, mutate } = useAnimationDownloadStatus(item.id);
  const { canContentWrite } = useAccess();
  const { addToast } = useToast();
  const [pending, setPending] = React.useState(false);
  const paused = status?.state === "Paused";
  const percent = Math.min(100, Math.max(0, (status?.progress ?? 0) * 100));
  const title = item.animation?.name ?? item.title;

  const togglePause = async () => {
    setPending(true);
    try {
      await (paused ? resumeDownload(item.id) : pauseDownload(item.id));
      await mutate();
    } catch {
      addToast({
        title: t(paused ? "toast.resumeFailed" : "toast.pauseFailed"),
        color: "danger",
      });
    } finally {
      setPending(false);
    }
  };

  return (
    <article className="border-b border-border-light py-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h3
            className="line-clamp-2 text-xs font-semibold leading-body text-foreground"
            title={title}
          >
            {title}
          </h3>
          {item.animation ? (
            <p
              className="mt-1 line-clamp-1 text-[11px] text-subtle"
              title={item.title}
            >
              {item.title}
            </p>
          ) : null}
        </div>
        {canContentWrite &&
        !error &&
        status &&
        (status.state === "Downloading" || paused) ? (
          <button
            type="button"
            disabled={pending}
            onClick={() => void togglePause()}
            aria-label={t(
              paused ? "workbench.resumeDownload" : "workbench.pauseDownload",
              { name: title },
            )}
            className="flex shrink-0 items-center gap-1 rounded border border-border px-2 py-1 text-[11px] text-brand hover:bg-tint focus:outline-hidden focus-visible:ring-2 focus-visible:ring-focus disabled:cursor-wait disabled:opacity-60"
          >
            {pending ? (
              <Spinner size={11} />
            ) : paused ? (
              <Play size={10} aria-hidden="true" />
            ) : (
              <Pause size={10} aria-hidden="true" />
            )}
            {t(paused ? "actions.resume" : "actions.pause")}
          </button>
        ) : null}
      </div>
      {error ? (
        <p className="mt-3 text-xs text-error" role="alert">
          {t("workbench.statusUnavailable")}
        </p>
      ) : !status ? (
        <div
          className="mt-3 flex items-center gap-2 text-[11px] text-muted"
          role="status"
        >
          <Spinner size={12} />
          {t("workbench.loading")}
        </div>
      ) : (
        <>
          <div
            className="mb-2 mt-3 h-1 overflow-hidden rounded-full bg-border-light"
            role="progressbar"
            aria-label={t("workbench.downloadProgress", { name: title })}
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={Math.round(percent)}
          >
            <div
              className={`h-full rounded-full ${status.state === "Error" ? "bg-error" : paused ? "bg-warning/60" : "bg-brand/70"}`}
              style={{ width: `${percent}%` }}
            />
          </div>
          <div className="flex items-center justify-between gap-2 text-[11px] tabular-nums text-subtle">
            <span>
              {status.state === "Downloading"
                ? formatBytes(status.speed)
                : t(`workbench.downloadStates.${status.state}`)}
            </span>
            <span>{Math.round(percent)}%</span>
          </div>
        </>
      )}
    </article>
  );
};

export const WorkbenchOverview: React.FC = () => {
  const { t } = useTranslation("animation");
  const downloads = useDownloadingAnimations(0, 3);
  const feeds = useFeeds();

  return (
    <aside
      className="grid min-w-0 gap-8 border-t border-border-light pt-7 sm:grid-cols-2 xl:grid-cols-1 xl:content-start xl:border-l xl:border-t-0 xl:pl-7 xl:pt-0"
      aria-label={t("workbench.overview")}
    >
      <section aria-labelledby="download-overview-title">
        <div className="mb-4 flex items-center justify-between gap-3">
          <h2
            id="download-overview-title"
            className="flex items-center gap-2 text-sm font-semibold text-foreground"
          >
            <Download size={15} className="text-brand" aria-hidden="true" />
            {t("workbench.downloads")}
          </h2>
          {downloads.data && !downloads.error ? (
            <span className="rounded bg-tint px-1.5 py-0.5 text-[11px] font-medium tabular-nums text-brand">
              {downloads.data.totalItems}
            </span>
          ) : null}
        </div>
        {downloads.error ? (
          <p className="py-3 text-xs leading-body text-error" role="alert">
            {t("workbench.downloadsFailed")}
          </p>
        ) : !downloads.data ? (
          <div
            className="flex items-center gap-2 py-3 text-xs text-muted"
            role="status"
          >
            <Spinner size={15} />
            {t("workbench.loading")}
          </div>
        ) : downloads.data.data.length === 0 ? (
          <p className="rounded-lg bg-tint px-3 py-4 text-xs leading-body text-muted">
            {t("empty.downloading")}
          </p>
        ) : (
          downloads.data.data.map((item) => (
            <DownloadSummary key={item.id} item={item} />
          ))
        )}
        <Link
          to="/downloading"
          className="mt-4 inline-flex items-center gap-1.5 rounded text-xs font-medium text-brand hover:underline focus:outline-hidden focus-visible:ring-2 focus-visible:ring-focus"
        >
          {t("workbench.allDownloads")}
          <ArrowRight size={14} aria-hidden="true" />
        </Link>
      </section>
      <section aria-labelledby="feed-overview-title">
        <div className="mb-4 flex items-center justify-between gap-3">
          <h2
            id="feed-overview-title"
            className="flex items-center gap-2 text-sm font-semibold text-foreground"
          >
            <Rss size={15} className="text-brand" aria-hidden="true" />
            {t("workbench.subscriptions")}
          </h2>
          {feeds.data && !feeds.error ? (
            <span className="rounded bg-tint px-1.5 py-0.5 text-[11px] font-medium tabular-nums text-brand">
              {feeds.data.length}
            </span>
          ) : null}
        </div>
        {feeds.error ? (
          <p className="py-3 text-xs leading-body text-error" role="alert">
            {t("workbench.subscriptionsFailed")}
          </p>
        ) : !feeds.data ? (
          <div
            className="flex items-center gap-2 py-3 text-xs text-muted"
            role="status"
          >
            <Spinner size={15} />
            {t("workbench.loading")}
          </div>
        ) : feeds.data.length === 0 ? (
          <p className="rounded-lg bg-tint px-3 py-4 text-xs leading-body text-muted">
            {t("workbench.noSubscriptions")}
          </p>
        ) : (
          <ul className="space-y-4">
            {feeds.data.slice(0, 4).map((feed) => (
              <li key={feed.id} className="border-l-2 border-brand/20 pl-3">
                <Link
                  to="/feeds"
                  className="block rounded focus:outline-hidden focus-visible:ring-2 focus-visible:ring-focus"
                >
                  <p className="line-clamp-2 break-all text-xs font-medium leading-body text-foreground hover:text-brand">
                    {feed.name || feed.url}
                  </p>
                  {feed.name ? (
                    <p className="mt-1 truncate text-[11px] text-subtle">
                      {feed.url}
                    </p>
                  ) : null}
                </Link>
              </li>
            ))}
          </ul>
        )}
        <Link
          to="/feeds"
          className="mt-4 inline-flex items-center gap-1.5 rounded text-xs font-medium text-brand hover:underline focus:outline-hidden focus-visible:ring-2 focus-visible:ring-focus"
        >
          {t("workbench.manageSubscriptions")}
          <ArrowRight size={14} aria-hidden="true" />
        </Link>
        <p className="mt-5 border-t border-border-light pt-4 text-[11px] leading-body text-subtle">
          {t("workbench.subscriptionHint")}
        </p>
      </section>
    </aside>
  );
};
