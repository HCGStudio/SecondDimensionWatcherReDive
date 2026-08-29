import dayjs from "dayjs";
import React from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";

import {
  AlertTriangle,
  BrainCircuit,
  CheckCircle2,
  CircleAlert,
  Download,
  FolderSync,
  HardDrive,
  Inbox,
  RefreshCw,
  Rss,
} from "lucide-react";

import { useToast } from "../components/ToastProvider";
import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Spinner } from "../components/ui/Spinner";
import { retryAllIncidents, retryIncident } from "../incidents/api";
import { useIncidents } from "../incidents/hooks";
import {
  Incident,
  IncidentSeverity,
  IncidentType,
  incidentTypes,
} from "../incidents/types";
import { cn } from "../lib/cn";
import { PageTemplate } from "./PageTemplate";

const typeIcons: Record<IncidentType, React.ReactNode> = {
  feedFailure: <Rss size={18} />,
  downloadStalled: <Download size={18} />,
  aiFailure: <BrainCircuit size={18} />,
  fileMappingFailure: <FolderSync size={18} />,
  diskSpaceLow: <HardDrive size={18} />,
};

const severityClasses: Record<IncidentSeverity, string> = {
  warning: "border-warning/25 bg-warning/10 text-warning",
  error: "border-error/25 bg-error/10 text-error",
  critical: "border-error/35 bg-error/15 text-error",
};

const IncidentCard: React.FC<{
  incident: Incident;
  retrying: boolean;
  focused: boolean;
  onRetry: () => void;
}> = ({ incident, retrying, focused, onRetry }) => {
  const { t } = useTranslation("incidents");
  const isResolved = incident.resolvedAt != null;

  return (
    <article
      id={`incident-${incident.id}`}
      tabIndex={focused ? -1 : undefined}
      className={cn(
        "rounded-xl border bg-surface p-4 shadow-whisper focus:outline-hidden focus:ring-2 focus:ring-focus",
        isResolved ? "border-border-light opacity-75" : "border-border",
        focused && "ring-2 ring-focus",
      )}
    >
      <div className="flex items-start gap-3">
        <div
          className={cn(
            "mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-lg",
            isResolved ? "bg-success/10 text-success" : "bg-canvas text-muted",
          )}
        >
          {isResolved ? <CheckCircle2 size={18} /> : typeIcons[incident.type]}
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-start justify-between gap-2">
            <div className="min-w-0">
              <div className="flex flex-wrap items-center gap-2">
                <h3 className="font-serif text-base font-medium leading-heading text-foreground">
                  {incident.title}
                </h3>
                <span
                  className={cn(
                    "rounded-full border px-2 py-0.5 text-[11px] font-medium",
                    severityClasses[incident.severity],
                  )}
                >
                  {t(`severity.${incident.severity}`)}
                </span>
                <span className="rounded-full bg-canvas px-2 py-0.5 text-[11px] text-muted">
                  {t(`types.${incident.type}`)}
                </span>
              </div>
              <p className="mt-1 text-xs text-subtle">
                {t(isResolved ? "status.resolved" : "status.open")} ·{" "}
                {dayjs(incident.detectedAt).fromNow()}
              </p>
            </div>
            {!isResolved && incident.canRetry ? (
              <Button
                variant="outline"
                size="sm"
                disabled={retrying}
                onClick={onRetry}
              >
                <RefreshCw
                  size={14}
                  className={retrying ? "animate-spin" : ""}
                />
                {retrying ? t("actions.retrying") : t("actions.retry")}
              </Button>
            ) : null}
          </div>

          <p className="mt-3 whitespace-pre-wrap text-sm leading-body text-muted">
            {incident.detail}
          </p>

          {incident.lastRetryError ? (
            <div className="mt-3 flex items-start gap-2 rounded-md bg-error/5 px-3 py-2 text-xs text-error">
              <CircleAlert size={14} className="mt-0.5 shrink-0" />
              <span>{incident.lastRetryError}</span>
            </div>
          ) : null}

          {incident.retryCount > 0 ? (
            <p className="mt-3 text-xs text-subtle">
              {t("retryHistory", {
                count: incident.retryCount,
                time: incident.lastRetryAt
                  ? dayjs(incident.lastRetryAt).fromNow()
                  : "—",
              })}
            </p>
          ) : null}
        </div>
      </div>
    </article>
  );
};

export const IncidentsPage: React.FC = () => {
  const { t } = useTranslation(["incidents", "errors"]);
  const { addToast } = useToast();
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedType = searchParams.get("type");
  const initialType = incidentTypes.includes(requestedType as IncidentType)
    ? (requestedType as IncidentType)
    : null;
  const focus = searchParams.get("focus");
  const [type, setType] = React.useState<IncidentType | null>(initialType);
  const [includeResolved, setIncludeResolved] = React.useState(false);
  const [page, setPage] = React.useState(0);
  const [retryingIds, setRetryingIds] = React.useState<Set<string>>(new Set());
  const [retryingAll, setRetryingAll] = React.useState(false);
  const { data, error, isLoading, mutate } = useIncidents({
    type,
    includeResolved,
    skip: page * 50,
    take: 50,
  });

  React.useEffect(() => {
    setPage(0);
  }, [includeResolved, type]);

  React.useEffect(() => {
    setType(initialType);
  }, [initialType]);

  React.useEffect(() => {
    if (!focus || !data) return;
    document.getElementById(`incident-${focus}`)?.scrollIntoView({
      behavior: "smooth",
      block: "center",
    });
  }, [data, focus]);

  const selectType = React.useCallback(
    (nextType: IncidentType | null) => {
      setType(nextType);
      const next = new URLSearchParams(searchParams);
      if (nextType) next.set("type", nextType);
      else next.delete("type");
      next.delete("focus");
      setSearchParams(next, { replace: true });
    },
    [searchParams, setSearchParams],
  );

  React.useEffect(() => {
    if (!data || page === 0 || page * 50 < data.totalCount) return;
    setPage(Math.max(0, Math.ceil(data.totalCount / 50) - 1));
  }, [data, page]);

  const onRetry = React.useCallback(
    async (incident: Incident) => {
      if (retryingAll) return;
      setRetryingIds((current) => new Set(current).add(incident.id));
      try {
        await retryIncident(incident.id);
        void mutate();
        addToast({
          title: t("incidents:toast.retrySucceeded"),
          color: "success",
        });
      } catch {
        void mutate();
        addToast({
          title: t("incidents:toast.retryFailed"),
          color: "danger",
        });
      } finally {
        setRetryingIds((current) => {
          const next = new Set(current);
          next.delete(incident.id);
          return next;
        });
      }
    },
    [addToast, mutate, retryingAll, t],
  );

  const onRetryAll = React.useCallback(async () => {
    if (retryingIds.size > 0) return;
    setRetryingAll(true);
    try {
      const result = await retryAllIncidents();
      void mutate();
      addToast({
        title: t("incidents:toast.retryAllFinished", {
          succeeded: result.succeeded,
          failed: result.failed,
        }),
        color: result.failed > 0 ? "warning" : "success",
      });
    } catch {
      addToast({
        title: t("incidents:toast.retryAllFailed"),
        color: "danger",
      });
    } finally {
      setRetryingAll(false);
    }
  }, [addToast, mutate, retryingIds.size, t]);

  return (
    <PageTemplate>
      <header className="mb-6 flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
        <div>
          <div className="flex items-center gap-2">
            <Inbox size={22} className="text-accent" />
            <h2 className="font-serif text-xl font-medium text-foreground">
              {t("incidents:title")}
            </h2>
          </div>
          <p className="mt-2 max-w-2xl text-sm leading-body text-muted">
            {t("incidents:subtitle")}
          </p>
        </div>
        <Button
          disabled={
            retryingAll || retryingIds.size > 0 || !data || data.openCount === 0
          }
          onClick={onRetryAll}
        >
          <RefreshCw size={16} className={retryingAll ? "animate-spin" : ""} />
          {retryingAll
            ? t("incidents:actions.retryingAll")
            : t("incidents:actions.retryAll")}
        </Button>
      </header>

      <section className="mb-5 rounded-xl border border-border bg-surface p-4 shadow-ring">
        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            onClick={() => selectType(null)}
            aria-pressed={type == null}
            className={cn(
              "rounded-full px-3 py-1.5 text-xs font-medium transition-colors",
              type == null
                ? "bg-brand text-surface"
                : "bg-canvas text-muted hover:text-foreground",
            )}
          >
            {t("incidents:filters.all")}
            {data ? ` · ${data.openCount}` : ""}
          </button>
          {incidentTypes.map((incidentType) => (
            <button
              key={incidentType}
              type="button"
              onClick={() => selectType(incidentType)}
              aria-pressed={type === incidentType}
              className={cn(
                "rounded-full px-3 py-1.5 text-xs font-medium transition-colors",
                type === incidentType
                  ? "bg-brand text-surface"
                  : "bg-canvas text-muted hover:text-foreground",
              )}
            >
              {t(`incidents:types.${incidentType}`)}
              {data?.countsByType?.[incidentType] != null
                ? ` · ${data.countsByType[incidentType]}`
                : ""}
            </button>
          ))}
          <label className="ml-auto inline-flex cursor-pointer items-center gap-2 text-xs text-muted">
            <input
              type="checkbox"
              checked={includeResolved}
              onChange={(event) => setIncludeResolved(event.target.checked)}
              className="h-4 w-4 accent-brand"
            />
            {t("incidents:filters.includeResolved")}
          </label>
        </div>
      </section>

      {error ? (
        <EmptyPrompt
          icon={<AlertTriangle size={44} />}
          title={<h3>{t("errors:loadFailed")}</h3>}
          body={<p>{t("incidents:errors.loadFailed")}</p>}
        />
      ) : isLoading || !data ? (
        <div className="flex justify-center py-16">
          <Spinner />
        </div>
      ) : data.items.length === 0 ? (
        <EmptyPrompt
          icon={<CheckCircle2 size={44} />}
          title={<h3>{t("incidents:empty.title")}</h3>}
          body={<p>{t("incidents:empty.body")}</p>}
        />
      ) : (
        <div className="space-y-3">
          {data.items.map((incident) => (
            <IncidentCard
              key={incident.id}
              incident={incident}
              focused={focus === incident.id}
              retrying={retryingAll || retryingIds.has(incident.id)}
              onRetry={() => void onRetry(incident)}
            />
          ))}
          {data.totalCount > 50 ? (
            <nav
              className="flex items-center justify-between gap-3 pt-3"
              aria-label={t("incidents:pagination.label")}
            >
              <Button
                variant="outline"
                size="sm"
                disabled={page === 0}
                onClick={() => setPage((current) => Math.max(0, current - 1))}
              >
                {t("incidents:pagination.previous")}
              </Button>
              <span className="text-xs text-muted">
                {t("incidents:pagination.summary", {
                  start: page * 50 + 1,
                  end: Math.min((page + 1) * 50, data.totalCount),
                  total: data.totalCount,
                })}
              </span>
              <Button
                variant="outline"
                size="sm"
                disabled={(page + 1) * 50 >= data.totalCount}
                onClick={() => setPage((current) => current + 1)}
              >
                {t("incidents:pagination.next")}
              </Button>
            </nav>
          ) : null}
        </div>
      )}
    </PageTemplate>
  );
};
