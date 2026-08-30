import React from "react";
import { useTranslation } from "react-i18next";

import {
  AlertTriangle,
  CheckCircle2,
  Loader2,
  Play,
  RotateCcw,
} from "lucide-react";

import { useToast } from "../components/ToastProvider";
import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Spinner } from "../components/ui/Spinner";
import { Table, type TableColumn } from "../components/ui/Table";
import { useDeadLetterJobs, useTasks } from "../tasks/hooks";
import { useTaskMetadata } from "../tasks/taskMetadata";
import { IDurableJob, ITask } from "../tasks/types";
import { resolveJobs, retryJobs, runTask } from "../tasks/utils";
import { PageTemplate } from "./PageTemplate";

function useFormatInterval(): (interval: string) => string {
  const { t } = useTranslation("tasks");
  return (interval: string): string => {
    const tokens = (days: number, hours: number, minutes: number): string => {
      const parts: string[] = [];
      if (days > 0) parts.push(t("interval.day", { count: days }));
      if (hours > 0) parts.push(t("interval.hour", { count: hours }));
      if (minutes > 0) parts.push(t("interval.minute", { count: minutes }));
      return parts.join(" ") || t("interval.lessThanMinute");
    };

    const match = interval.match(/^(\d+)\.?(\d{2}):(\d{2}):(\d{2})$/);
    if (match) {
      const [, days, hours, minutes] = match;
      return tokens(parseInt(days), parseInt(hours), parseInt(minutes));
    }
    const match2 = interval.match(/^(\d{2}):(\d{2}):(\d{2})$/);
    if (match2) {
      const [, hours, minutes] = match2;
      return tokens(0, parseInt(hours), parseInt(minutes));
    }
    return interval;
  };
}

export const TasksPage: React.FC = () => {
  const { t } = useTranslation(["tasks", "errors"]);
  const getTaskMetadata = useTaskMetadata();
  const formatInterval = useFormatInterval();
  const { data: tasks, error, mutate } = useTasks();
  const {
    data: deadLetters,
    error: deadLetterError,
    mutate: mutateDeadLetters,
  } = useDeadLetterJobs();
  const { addToast } = useToast();
  const [runningTasks, setRunningTasks] = React.useState<Set<string>>(
    new Set(),
  );
  const [mutatingJobs, setMutatingJobs] = React.useState<Set<string>>(
    new Set(),
  );

  const onRun = React.useCallback(
    async (id: string) => {
      setRunningTasks((prev) => new Set(prev).add(id));
      try {
        await runTask(id);
        await mutate();
        addToast({
          title: t("tasks:toast.success", { name: getTaskMetadata(id).name }),
          color: "success",
        });
      } catch {
        addToast({
          title: t("tasks:toast.failure", { name: getTaskMetadata(id).name }),
          color: "danger",
        });
      } finally {
        setRunningTasks((prev) => {
          const next = new Set(prev);
          next.delete(id);
          return next;
        });
      }
    },
    [mutate, addToast, t, getTaskMetadata],
  );

  const mutateJob = React.useCallback(
    async (job: IDurableJob, action: "retry" | "resolve") => {
      setMutatingJobs((previous) => new Set(previous).add(job.id));
      try {
        const result =
          action === "retry"
            ? await retryJobs([job.id])
            : await resolveJobs([job.id]);
        if (result.affectedCount !== 1) throw new Error("job state changed");
        await mutateDeadLetters();
        addToast({
          title: t(`tasks:deadLetters.toast.${action}Success`),
          color: "success",
        });
      } catch {
        addToast({
          title: t(`tasks:deadLetters.toast.${action}Failure`),
          color: "danger",
        });
      } finally {
        setMutatingJobs((previous) => {
          const next = new Set(previous);
          next.delete(job.id);
          return next;
        });
      }
    },
    [addToast, mutateDeadLetters, t],
  );

  const columns: TableColumn<ITask>[] = [
    {
      name: t("tasks:columns.name"),
      render: (_value: any, item: ITask) => getTaskMetadata(item.id).name,
    },
    {
      name: t("tasks:columns.description"),
      render: (_value: any, item: ITask) =>
        getTaskMetadata(item.id).description,
    },
    {
      field: "interval",
      name: t("tasks:columns.interval"),
      render: (value: string) => formatInterval(value),
    },
    {
      field: "lastRunAt",
      name: t("tasks:columns.lastRun"),
      render: (value: string | null) =>
        value ? new Date(value).toLocaleString() : "-",
    },
    {
      name: t("tasks:columns.status"),
      render: (_value: any, item: ITask) =>
        item.isRunning || runningTasks.has(item.id) ? (
          <span className="inline-flex items-center gap-1.5 text-sm text-brand">
            <Loader2 size={14} className="animate-spin" />
            {t("tasks:running")}
          </span>
        ) : (
          <span className="text-sm text-success">{t("tasks:idle")}</span>
        ),
      width: "100px",
    },
    {
      name: t("tasks:columns.actions"),
      render: (_value: any, item: ITask) => (
        <Button
          size="sm"
          variant="outline"
          disabled={item.isRunning || runningTasks.has(item.id)}
          onClick={() => onRun(item.id)}
        >
          <Play size={14} />
          {t("tasks:runNow")}
        </Button>
      ),
      width: "130px",
    },
  ];

  const deadLetterColumns: TableColumn<IDurableJob>[] = [
    {
      field: "type",
      name: t("tasks:deadLetters.columns.type"),
      render: () => t("tasks:deadLetters.types.downloadCompletion"),
    },
    {
      field: "stage",
      name: t("tasks:deadLetters.columns.stage"),
      render: (value: IDurableJob["stage"]) =>
        t(`tasks:deadLetters.stages.${value}`),
    },
    {
      field: "attemptCount",
      name: t("tasks:deadLetters.columns.attempts"),
    },
    {
      field: "updatedAt",
      name: t("tasks:deadLetters.columns.updated"),
      render: (value: string) => new Date(value).toLocaleString(),
    },
    {
      field: "lastError",
      name: t("tasks:deadLetters.columns.error"),
      render: (value: string | null) => value ?? "-",
      truncateText: true,
    },
    {
      name: t("tasks:deadLetters.columns.actions"),
      render: (_value: unknown, item: IDurableJob) => {
        const disabled = mutatingJobs.has(item.id);
        return (
          <div className="flex gap-2">
            <Button
              size="sm"
              variant="outline"
              disabled={disabled}
              onClick={() => mutateJob(item, "retry")}
            >
              <RotateCcw size={14} />
              {t("tasks:deadLetters.retry")}
            </Button>
            <Button
              size="sm"
              variant="outline"
              color="success"
              disabled={disabled}
              onClick={() => mutateJob(item, "resolve")}
            >
              <CheckCircle2 size={14} />
              {t("tasks:deadLetters.resolve")}
            </Button>
          </div>
        );
      },
      width: "230px",
    },
  ];

  return (
    <PageTemplate>
      <h2 className="mb-6 font-serif text-xl font-medium text-foreground">
        {t("tasks:title")}
      </h2>
      {error ? (
        <EmptyPrompt
          icon={<AlertTriangle size={48} />}
          title={<h2>{t("errors:loadFailed")}</h2>}
          body={<p>{t("tasks:loadFailed")}</p>}
        />
      ) : !tasks ? (
        <div className="flex justify-center py-8">
          <Spinner />
        </div>
      ) : tasks.length > 0 ? (
        <Table items={tasks} columns={columns} />
      ) : (
        <EmptyPrompt
          title={<h2>{t("tasks:empty.title")}</h2>}
          body={<p>{t("tasks:empty.body")}</p>}
        />
      )}

      <h2 className="mb-2 mt-10 font-serif text-xl font-medium text-foreground">
        {t("tasks:deadLetters.title")}
      </h2>
      <p className="mb-5 text-sm text-muted">
        {t("tasks:deadLetters.description")}
      </p>
      {deadLetterError ? (
        <p className="text-sm text-error">
          {t("tasks:deadLetters.loadFailed")}
        </p>
      ) : !deadLetters ? (
        <div className="flex justify-center py-8">
          <Spinner />
        </div>
      ) : deadLetters.items.length > 0 ? (
        <Table items={deadLetters.items} columns={deadLetterColumns} />
      ) : (
        <p className="rounded-md border border-border-light bg-surface px-4 py-6 text-sm text-muted">
          {t("tasks:deadLetters.empty")}
        </p>
      )}
    </PageTemplate>
  );
};
