import React from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useSearchParams } from "react-router";

import {
  AlertTriangle,
  BellRing,
  CheckCheck,
  Clock3,
  Download,
  ExternalLink,
  Inbox,
} from "lucide-react";

import { submitDownload } from "../animation/utils";
import { useToast } from "../components/ToastProvider";
import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Spinner } from "../components/ui/Spinner";
import { cn } from "../lib/cn";
import { updateTodoState } from "../todos/api";
import { useTodos } from "../todos/hooks";
import { getTodoSnoozeAction } from "../todos/state";
import { TodoItem, TodoPriority } from "../todos/types";
import { PageTemplate } from "./PageTemplate";

const priorityClass: Record<TodoPriority, string> = {
  Normal: "border-border bg-surface",
  High: "border-warning/40 bg-warning/5",
  Critical: "border-error/40 bg-error/5",
};

const PAGE_SIZE = 50;

export const TodoPage: React.FC = () => {
  const { t, i18n } = useTranslation(["todos", "errors", "common"]);
  const { addToast } = useToast();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const focus = searchParams.get("focus");
  const [includeRead, setIncludeRead] = React.useState(false);
  const [includeSnoozed, setIncludeSnoozed] = React.useState(false);
  const [page, setPage] = React.useState(0);
  const [selected, setSelected] = React.useState<Set<string>>(new Set());
  const [busy, setBusy] = React.useState(false);
  const { data, error, mutate } = useTodos({
    includeRead,
    includeSnoozed,
    skip: page * PAGE_SIZE,
    take: PAGE_SIZE,
  });

  React.useEffect(() => {
    setPage(0);
    setSelected(new Set());
  }, [includeRead, includeSnoozed]);

  React.useEffect(() => {
    if (!data || page === 0 || page * PAGE_SIZE < data.totalCount) return;
    setPage(Math.max(0, Math.ceil(data.totalCount / PAGE_SIZE) - 1));
  }, [data, page]);

  React.useEffect(() => {
    if (!focus || !data) return;
    document.getElementById(`todo-${focus}`)?.scrollIntoView({
      behavior: "smooth",
      block: "center",
    });
  }, [data, focus]);

  const apply = React.useCallback(
    async (
      keys: string[],
      action: "markRead" | "markUnread" | "snooze" | "unsnooze",
    ) => {
      if (!keys.length || busy) return;
      setBusy(true);
      try {
        await updateTodoState(
          keys,
          action,
          action === "snooze"
            ? new Date(Date.now() + 60 * 60 * 1000).toISOString()
            : undefined,
        );
        setSelected(new Set());
        await mutate();
      } catch {
        addToast({ title: t("todos:toast.updateFailed"), color: "danger" });
      } finally {
        setBusy(false);
      }
    },
    [addToast, busy, mutate, t],
  );

  const download = React.useCallback(
    async (item: TodoItem) => {
      if (!item.resourceId || busy) return;
      setBusy(true);
      try {
        await submitDownload(item.resourceId);
        await updateTodoState([item.key], "markRead");
        await mutate();
        addToast({ title: t("todos:toast.downloadStarted"), color: "success" });
      } catch {
        addToast({ title: t("todos:toast.downloadFailed"), color: "danger" });
      } finally {
        setBusy(false);
      }
    },
    [addToast, busy, mutate, t],
  );

  const items = data?.items ?? [];
  const allSelected =
    items.length > 0 && items.every((item) => selected.has(item.key));

  return (
    <PageTemplate>
      <header className="mb-7 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <p className="mb-2 text-xs font-medium uppercase tracking-[0.12em] text-brand">
            {t("todos:eyebrow")}
          </p>
          <h1 className="font-serif text-2xl font-medium text-foreground">
            {t("todos:title")}
          </h1>
          <p className="mt-2 max-w-2xl text-sm leading-body text-muted">
            {t("todos:subtitle")}
          </p>
        </div>
        <div className="rounded-lg border border-border bg-surface px-4 py-2 text-sm text-muted">
          {t("todos:unread", { count: data?.unreadCount ?? 0 })}
        </div>
      </header>

      <div className="mb-5 flex flex-col gap-3 rounded-lg border border-border bg-surface p-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex flex-wrap items-center gap-4 text-sm text-muted">
          <label className="inline-flex items-center gap-2">
            <input
              type="checkbox"
              className="h-4 w-4 accent-brand"
              checked={allSelected}
              onChange={(event) =>
                setSelected(
                  event.target.checked
                    ? new Set(items.map((item) => item.key))
                    : new Set(),
                )
              }
            />
            {t("todos:actions.selectAll")}
          </label>
          <label className="inline-flex items-center gap-2">
            <input
              type="checkbox"
              className="h-4 w-4 accent-brand"
              checked={includeRead}
              onChange={(event) => setIncludeRead(event.target.checked)}
            />
            {t("todos:filters.includeRead")}
          </label>
          <label className="inline-flex items-center gap-2">
            <input
              type="checkbox"
              className="h-4 w-4 accent-brand"
              checked={includeSnoozed}
              onChange={(event) => setIncludeSnoozed(event.target.checked)}
            />
            {t("todos:filters.includeSnoozed")}
          </label>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button
            size="sm"
            variant="outline"
            disabled={!selected.size || busy}
            onClick={() => void apply([...selected], "snooze")}
          >
            <Clock3 size={14} />
            {t("todos:actions.snoozeSelected")}
          </Button>
          <Button
            size="sm"
            disabled={!selected.size || busy}
            onClick={() => void apply([...selected], "markRead")}
          >
            <CheckCheck size={14} />
            {t("todos:actions.readSelected")}
          </Button>
        </div>
      </div>

      {error ? (
        <EmptyPrompt
          icon={<AlertTriangle size={48} />}
          title={<h2>{t("errors:loadFailed")}</h2>}
          body={<p>{t("todos:errors.loadFailed")}</p>}
        />
      ) : !data ? (
        <div className="flex justify-center py-24">
          <Spinner />
        </div>
      ) : !items.length ? (
        <EmptyPrompt
          icon={<Inbox size={48} />}
          title={<h2>{t("todos:empty.title")}</h2>}
          body={<p>{t("todos:empty.body")}</p>}
        />
      ) : (
        <div>
          <ul className="space-y-3" aria-label={t("todos:listLabel")}>
            {items.map((item) => {
              const automation =
                item.type === "ReleaseMatched" ||
                item.type === "DownloadPendingConfirmation" ||
                item.type === "DownloadFailed";
              const detail = automation
                ? t(`todos:details.${item.type}`)
                : item.detail;
              const snoozeAction = getTodoSnoozeAction(item.snoozedUntil);
              const isSnoozed = snoozeAction === "unsnooze";
              return (
                <li
                  id={`todo-${item.key}`}
                  key={item.key}
                  tabIndex={focus === item.key ? -1 : undefined}
                  className={cn(
                    "rounded-xl border p-4 shadow-whisper transition-shadow focus:outline-hidden focus:ring-2 focus:ring-focus sm:p-5",
                    priorityClass[item.priority],
                    item.readAt && "opacity-65",
                    focus === item.key && "ring-2 ring-focus",
                  )}
                >
                  <div className="flex items-start gap-3">
                    <input
                      type="checkbox"
                      aria-label={t("todos:actions.selectItem", {
                        title: item.title,
                      })}
                      className="mt-1 h-4 w-4 shrink-0 accent-brand"
                      checked={selected.has(item.key)}
                      onChange={(event) =>
                        setSelected((current) => {
                          const next = new Set(current);
                          if (event.target.checked) next.add(item.key);
                          else next.delete(item.key);
                          return next;
                        })
                      }
                    />
                    <div className="min-w-0 flex-1">
                      <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                        <div>
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="rounded-full bg-canvas px-2 py-0.5 text-xs font-medium text-muted">
                              {t(`todos:types.${item.type}`)}
                            </span>
                            <span className="text-xs text-subtle">
                              {t(`todos:priorities.${item.priority}`)}
                            </span>
                          </div>
                          <h2 className="mt-2 font-serif text-lg font-medium text-foreground">
                            {item.title}
                          </h2>
                        </div>
                        <time
                          className="shrink-0 text-xs text-subtle"
                          dateTime={item.occurredAt}
                        >
                          {new Date(item.occurredAt).toLocaleString(
                            i18n.resolvedLanguage,
                          )}
                        </time>
                      </div>
                      <p className="mt-2 text-sm leading-body text-muted">
                        {detail}
                      </p>
                      <div className="mt-4 flex flex-wrap gap-2">
                        {automation ? (
                          <Button
                            size="sm"
                            disabled={busy}
                            onClick={() => void download(item)}
                          >
                            <Download size={14} />
                            {t("todos:actions.download")}
                          </Button>
                        ) : (
                          <Button
                            size="sm"
                            onClick={() => navigate(item.deepLink)}
                          >
                            <ExternalLink size={14} />
                            {t("todos:actions.open")}
                          </Button>
                        )}
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={busy}
                          onClick={() =>
                            void apply(
                              [item.key],
                              item.readAt ? "markUnread" : "markRead",
                            )
                          }
                        >
                          <CheckCheck size={14} />
                          {item.readAt
                            ? t("todos:actions.markUnread")
                            : t("todos:actions.markRead")}
                        </Button>
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={busy}
                          onClick={() => void apply([item.key], snoozeAction)}
                        >
                          {isSnoozed ? (
                            <BellRing size={14} />
                          ) : (
                            <Clock3 size={14} />
                          )}
                          {isSnoozed
                            ? t("todos:actions.unsnooze")
                            : t("todos:actions.snooze")}
                        </Button>
                      </div>
                    </div>
                  </div>
                </li>
              );
            })}
          </ul>
          {data.totalCount > PAGE_SIZE ? (
            <nav
              className="mt-4 flex items-center justify-between gap-3"
              aria-label={t("common:pagination.page", { page: page + 1 })}
            >
              <Button
                variant="outline"
                size="sm"
                disabled={page === 0 || busy}
                onClick={() => setPage((current) => Math.max(0, current - 1))}
              >
                {t("common:pagination.prev")}
              </Button>
              <span className="text-xs text-muted">
                {t("common:pagination.page", { page: page + 1 })}
              </span>
              <Button
                variant="outline"
                size="sm"
                disabled={busy || (page + 1) * PAGE_SIZE >= data.totalCount}
                onClick={() => setPage((current) => current + 1)}
              >
                {t("common:pagination.next")}
              </Button>
            </nav>
          ) : null}
        </div>
      )}
    </PageTemplate>
  );
};
