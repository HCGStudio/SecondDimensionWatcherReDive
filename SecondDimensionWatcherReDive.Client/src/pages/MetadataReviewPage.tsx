import React from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";

import {
  AlertTriangle,
  CheckCircle2,
  Clock3,
  Edit3,
  Files,
  History,
  RefreshCw,
  RotateCcw,
} from "lucide-react";

import { tmdbImageUrl } from "../animation/tmdbImage";
import { MetadataReviewSheet } from "../components/MetadataReviewSheet";
import { ResilientPoster } from "../components/ResilientPoster";
import { useToast } from "../components/ToastProvider";
import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Pagination } from "../components/ui/Pagination";
import { Spinner } from "../components/ui/Spinner";
import { cn } from "../lib/cn";
import {
  METADATA_REVIEW_PAGE_SIZE,
  metadataReviewErrorStatus,
  undoMetadataRemap,
  useMetadataReview,
} from "../metadataReview/api";
import {
  MetadataRemapResult,
  MetadataReviewCounts,
  MetadataReviewItem,
  MetadataReviewOperation,
  MetadataReviewStatus,
} from "../metadataReview/types";
import { PageTemplate } from "./PageTemplate";

const REVIEW_STATUSES: MetadataReviewStatus[] = [
  "pending",
  "lowConfidence",
  "failed",
];

const EMPTY_COUNTS: MetadataReviewCounts = {
  pending: 0,
  lowConfidence: 0,
  failed: 0,
};

function parseStatus(value: string | null): MetadataReviewStatus {
  return REVIEW_STATUSES.includes(value as MetadataReviewStatus)
    ? (value as MetadataReviewStatus)
    : "pending";
}

function parsePage(value: string | null): number {
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : 1;
}

interface StatusTabsProps {
  active: MetadataReviewStatus;
  counts: MetadataReviewCounts;
  countsLoaded: boolean;
  onChange: (status: MetadataReviewStatus) => void;
}

const StatusTabs: React.FC<StatusTabsProps> = ({
  active,
  counts,
  countsLoaded,
  onChange,
}) => {
  const { t } = useTranslation("metadataReview");

  return (
    <div
      role="tablist"
      aria-label={t("tabs.aria")}
      className="grid grid-cols-3 gap-2 rounded-xl border border-border-light bg-surface p-1.5 shadow-whisper"
    >
      {REVIEW_STATUSES.map((status) => (
        <button
          id={`metadata-review-tab-${status}`}
          key={status}
          type="button"
          role="tab"
          aria-selected={active === status}
          aria-controls="metadata-review-panel"
          tabIndex={active === status ? 0 : -1}
          onClick={() => onChange(status)}
          onKeyDown={(event) => {
            const currentIndex = REVIEW_STATUSES.indexOf(status);
            let nextIndex: number | null = null;
            if (event.key === "ArrowRight" || event.key === "ArrowDown") {
              nextIndex = (currentIndex + 1) % REVIEW_STATUSES.length;
            } else if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
              nextIndex =
                (currentIndex - 1 + REVIEW_STATUSES.length) %
                REVIEW_STATUSES.length;
            } else if (event.key === "Home") {
              nextIndex = 0;
            } else if (event.key === "End") {
              nextIndex = REVIEW_STATUSES.length - 1;
            }
            if (nextIndex == null) return;
            event.preventDefault();
            const nextStatus = REVIEW_STATUSES[nextIndex];
            onChange(nextStatus);
            window.requestAnimationFrame(() =>
              document
                .getElementById(`metadata-review-tab-${nextStatus}`)
                ?.focus(),
            );
          }}
          className={cn(
            "flex min-w-0 items-center justify-center gap-2 rounded-lg px-3 py-2.5 text-sm font-medium transition-colors",
            active === status
              ? "bg-canvas text-foreground shadow-ring"
              : "text-muted hover:bg-canvas/60 hover:text-foreground",
          )}
        >
          <span className="truncate">{t(`tabs.${status}`)}</span>
          <span
            className={cn(
              "min-w-6 rounded-full px-1.5 py-0.5 text-center text-xs tabular-nums",
              active === status
                ? "bg-brand/10 text-brand"
                : "bg-canvas text-subtle",
            )}
          >
            {countsLoaded ? counts[status] : "…"}
          </span>
        </button>
      ))}
    </div>
  );
};

function confidenceLabel(confidence: number | null): string | null {
  if (confidence == null || !Number.isFinite(confidence)) return null;
  const percentage = confidence <= 1 ? confidence * 100 : confidence;
  return `${Math.round(percentage)}%`;
}

interface ReviewItemRowProps {
  item: MetadataReviewItem;
  onEdit: (trigger: HTMLButtonElement) => void;
}

const ReviewItemRow: React.FC<ReviewItemRowProps> = ({ item, onEdit }) => {
  const { t } = useTranslation("metadataReview");
  const poster = tmdbImageUrl(item.metadata.posterPath, "w185");
  const confidence = confidenceLabel(item.confidence);

  return (
    <article className="group p-4 sm:p-5">
      <div className="flex items-start gap-4">
        <ResilientPoster
          src={poster}
          alt=""
          className="hidden h-24 w-16 rounded-lg sm:flex"
        />

        <div className="min-w-0 flex-1">
          <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
            <div className="min-w-0">
              <div className="flex flex-wrap items-center gap-2">
                <span
                  className={cn(
                    "rounded-full px-2 py-0.5 text-xs font-medium",
                    item.reviewStatus === "failed"
                      ? "bg-error/10 text-error"
                      : item.reviewStatus === "lowConfidence"
                        ? "bg-warning/15 text-warning"
                        : "bg-brand/10 text-brand",
                  )}
                >
                  {t(`tabs.${item.reviewStatus}`)}
                </span>
                {confidence ? (
                  <span className="text-xs text-subtle">
                    {t("item.confidence", { confidence })}
                  </span>
                ) : null}
                <span className="text-xs text-subtle">
                  {new Date(item.publishTime).toLocaleString()}
                </span>
              </div>
              <h3
                className="mt-2 line-clamp-2 font-serif text-base font-medium leading-heading text-foreground sm:text-lg"
                title={item.title}
              >
                {item.title}
              </h3>
              {item.description ? (
                <p className="mt-1 line-clamp-2 text-sm leading-body text-muted">
                  {item.description}
                </p>
              ) : null}
            </div>

            <Button
              variant="outline"
              size="sm"
              className="shrink-0 self-start"
              onClick={(event) => onEdit(event.currentTarget)}
            >
              <Edit3 size={15} />
              {t("item.review")}
            </Button>
          </div>

          {item.failureReason ? (
            <div className="mt-3 flex items-start gap-2 rounded-lg bg-error/8 px-3 py-2 text-sm text-error">
              <AlertTriangle size={15} className="mt-0.5 shrink-0" />
              <span>{item.failureReason}</span>
            </div>
          ) : null}

          <div className="mt-3 flex flex-wrap gap-x-5 gap-y-2 text-xs text-muted">
            <span>
              <strong className="font-medium text-foreground">
                {item.metadata.name ?? t("values.notResolved")}
              </strong>
              {item.metadata.originalName ? (
                <span className="ml-1 text-subtle">
                  · {item.metadata.originalName}
                </span>
              ) : null}
            </span>
            <span>
              {t("fields.tmdbId")}: {item.metadata.tmdbId ?? t("values.unset")}
            </span>
            <span>
              {t("item.seasonEpisode", {
                season: item.metadata.season ?? "—",
                episode: item.metadata.episode ?? "—",
              })}
            </span>
            <span>
              {t("fields.groupName")}:{" "}
              {item.metadata.groupName ?? t("values.unset")}
            </span>
          </div>

          <div className="mt-3 flex flex-wrap items-center gap-2 border-t border-border-light pt-3 text-xs text-subtle">
            <span className="inline-flex items-center gap-1.5">
              <Files size={13} />
              {t("item.mappedFiles", { count: item.mappedFileCount })}
            </span>
            <span className="text-border">·</span>
            <span
              className={
                item.isDownloadFinished ? "text-success" : "text-subtle"
              }
            >
              {item.isDownloadFinished
                ? t("item.downloaded")
                : t("item.notDownloaded")}
            </span>
            <span className="text-border">·</span>
            <span>{t("item.retryCount", { count: item.aiRetryCount })}</span>
            <span className="text-border">·</span>
            <span>{t("item.revision", { revision: item.revision })}</span>
          </div>
        </div>
      </div>
    </article>
  );
};

interface RecentOperationsProps {
  operations: MetadataReviewOperation[];
  undoing: Set<string>;
  onUndo: (operation: MetadataReviewOperation) => void;
}

const RecentOperations: React.FC<RecentOperationsProps> = ({
  operations,
  undoing,
  onUndo,
}) => {
  const { t } = useTranslation("metadataReview");

  return (
    <section className="mt-6 rounded-xl border border-border-light bg-surface p-4 shadow-whisper sm:p-5">
      <div className="flex items-start gap-3">
        <span className="rounded-lg bg-canvas p-2 text-muted">
          <History size={18} />
        </span>
        <div>
          <h2 className="font-serif text-base font-medium text-foreground">
            {t("recent.title")}
          </h2>
          <p className="mt-0.5 text-sm text-muted">{t("recent.subtitle")}</p>
        </div>
      </div>

      {operations.length > 0 ? (
        <ul className="mt-4 divide-y divide-border-light border-t border-border-light">
          {operations.map((operation) => {
            const busy = undoing.has(operation.operationId);
            return (
              <li
                key={operation.operationId}
                className="flex flex-col gap-3 py-3 sm:flex-row sm:items-center sm:justify-between"
              >
                <div className="min-w-0">
                  <p className="truncate text-sm font-medium text-foreground">
                    {operation.title}
                  </p>
                  <p className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-subtle">
                    <span className="inline-flex items-center gap-1">
                      <Clock3 size={12} />
                      {new Date(operation.appliedAt).toLocaleString()}
                    </span>
                    <span>
                      {t("recent.revision", { revision: operation.revision })}
                    </span>
                  </p>
                </div>
                <Button
                  variant="outline"
                  size="sm"
                  className="shrink-0 self-start sm:self-auto"
                  disabled={!operation.canUndo || busy}
                  onClick={() => onUndo(operation)}
                >
                  {busy ? <Spinner size={14} /> : <RotateCcw size={14} />}
                  {busy
                    ? t("recent.undoing")
                    : operation.canUndo
                      ? t("recent.undo")
                      : t("recent.unavailable")}
                </Button>
              </li>
            );
          })}
        </ul>
      ) : (
        <p className="mt-4 border-t border-border-light pt-4 text-sm text-subtle">
          {t("recent.empty")}
        </p>
      )}
    </section>
  );
};

export const MetadataReviewPage: React.FC = () => {
  const { t } = useTranslation("metadataReview");
  const [searchParams, setSearchParams] = useSearchParams();
  const status = parseStatus(searchParams.get("status"));
  const page = parsePage(searchParams.get("page"));
  const { data, error, isLoading, mutate } = useMetadataReview(status, page);
  const { addToast } = useToast();
  const [selectedItem, setSelectedItem] =
    React.useState<MetadataReviewItem | null>(null);
  const reviewTriggerRef = React.useRef<HTMLButtonElement | null>(null);
  const queueHeadingRef = React.useRef<HTMLHeadingElement | null>(null);
  const [undoing, setUndoing] = React.useState<Set<string>>(new Set());

  const updateLocation = React.useCallback(
    (nextStatus: MetadataReviewStatus, nextPage: number) => {
      const next = new URLSearchParams(searchParams);
      if (nextStatus === "pending") next.delete("status");
      else next.set("status", nextStatus);
      if (nextPage === 1) next.delete("page");
      else next.set("page", String(nextPage));
      setSearchParams(next);
    },
    [searchParams, setSearchParams],
  );

  React.useEffect(() => {
    if (!data) return;
    const lastPage = Math.max(
      1,
      Math.ceil(data.totalItems / METADATA_REVIEW_PAGE_SIZE),
    );
    if (page > lastPage) updateLocation(status, lastPage);
  }, [data, page, status, updateLocation]);

  const changeStatus = React.useCallback(
    (nextStatus: MetadataReviewStatus) => {
      setSelectedItem(null);
      updateLocation(nextStatus, 1);
    },
    [updateLocation],
  );

  const handleApplied = React.useCallback(
    async (result: MetadataRemapResult) => {
      setSelectedItem(null);
      await mutate();
      addToast({
        title: t("toast.applied"),
        text: t("toast.appliedDetail", {
          count: result.pathChanges.length,
          revision: result.revision,
        }),
        color: "success",
      });
      queueHeadingRef.current?.focus();
    },
    [addToast, mutate, t],
  );

  const handleUndo = React.useCallback(
    async (operation: MetadataReviewOperation) => {
      setUndoing((current) => new Set(current).add(operation.operationId));
      try {
        await undoMetadataRemap(operation.operationId, operation.revision);
        await mutate();
        addToast({ title: t("toast.undone"), color: "success" });
      } catch (undoError) {
        const statusCode = metadataReviewErrorStatus(undoError);
        addToast({
          title:
            statusCode === 409
              ? t("toast.undoConflict")
              : statusCode === 422
                ? t("toast.undoValidation")
                : t("toast.undoFailed"),
          color: "danger",
        });
      } finally {
        setUndoing((current) => {
          const next = new Set(current);
          next.delete(operation.operationId);
          return next;
        });
      }
    },
    [addToast, mutate, t],
  );

  const counts = data?.counts ?? EMPTY_COUNTS;
  const pageCount = data
    ? Math.ceil(data.totalItems / METADATA_REVIEW_PAGE_SIZE)
    : 0;

  return (
    <PageTemplate>
      <header className="max-w-3xl">
        <h1 className="font-serif text-2xl font-medium leading-heading text-foreground">
          {t("title")}
        </h1>
        <p className="mt-2 text-sm leading-body text-muted">{t("subtitle")}</p>
      </header>

      <div className="mt-6">
        <StatusTabs
          active={status}
          counts={counts}
          countsLoaded={!!data}
          onChange={changeStatus}
        />
      </div>

      <RecentOperations
        operations={data?.recentOperations ?? []}
        undoing={undoing}
        onUndo={handleUndo}
      />

      <section
        id="metadata-review-panel"
        role="tabpanel"
        aria-labelledby={`metadata-review-tab-${status}`}
        className="mt-8"
      >
        <div className="mb-3 flex items-end justify-between gap-3">
          <div>
            <h2
              ref={queueHeadingRef}
              tabIndex={-1}
              className="font-serif text-lg font-medium text-foreground focus:outline-hidden"
            >
              {t(`queue.${status}.title`)}
            </h2>
            <p className="mt-1 text-sm text-muted">
              {data
                ? t("queue.total", { count: data.totalItems })
                : t("queue.loading")}
            </p>
          </div>
          {data ? (
            <span className="text-xs text-subtle">
              {t("queue.page", {
                page,
                total: Math.max(1, pageCount),
              })}
            </span>
          ) : null}
        </div>

        <div className="overflow-hidden rounded-xl border border-border-light bg-surface shadow-whisper">
          {error ? (
            <EmptyPrompt
              role="alert"
              icon={<AlertTriangle size={42} />}
              title={<h3>{t("errors.loadFailed")}</h3>}
              body={
                <div>
                  <p>{t("errors.loadFailedDetail")}</p>
                  <Button
                    className="mt-4"
                    variant="outline"
                    size="sm"
                    onClick={() => void mutate()}
                  >
                    <RefreshCw size={14} />
                    {t("errors.retry")}
                  </Button>
                </div>
              }
            />
          ) : isLoading || !data ? (
            <div className="flex justify-center py-20">
              <Spinner />
            </div>
          ) : data.data.length === 0 ? (
            <EmptyPrompt
              icon={<CheckCircle2 size={42} />}
              title={<h3>{t(`queue.${status}.emptyTitle`)}</h3>}
              body={<p>{t(`queue.${status}.emptyBody`)}</p>}
            />
          ) : (
            <div className="divide-y divide-border-light">
              {data.data.map((item) => (
                <ReviewItemRow
                  key={item.id}
                  item={item}
                  onEdit={(trigger) => {
                    reviewTriggerRef.current = trigger;
                    setSelectedItem(item);
                  }}
                />
              ))}
            </div>
          )}
        </div>

        {pageCount > 1 ? (
          <div className="mt-5 flex justify-center">
            <Pagination
              pageCount={pageCount}
              activePage={page - 1}
              onPageClick={(nextPage) => updateLocation(status, nextPage + 1)}
            />
          </div>
        ) : null}
      </section>

      <MetadataReviewSheet
        item={selectedItem}
        onOpenChange={(open) => {
          if (!open) setSelectedItem(null);
        }}
        onApplied={handleApplied}
        restoreFocusRef={reviewTriggerRef}
      />
    </PageTemplate>
  );
};
