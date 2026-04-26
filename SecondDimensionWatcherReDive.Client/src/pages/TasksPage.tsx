import { AlertTriangle, Loader2, Play } from "lucide-react";
import React from "react";
import { useTranslation } from "react-i18next";

import { useToast } from "../components/ToastProvider";
import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Spinner } from "../components/ui/Spinner";
import { Table, type TableColumn } from "../components/ui/Table";
import { useTasks } from "../tasks/hooks";
import { useTaskMetadata } from "../tasks/taskMetadata";
import { runTask } from "../tasks/utils";
import { ITask } from "../tasks/types";
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
  const { addToast } = useToast();
  const [runningTasks, setRunningTasks] = React.useState<Set<string>>(new Set());

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

  const columns: TableColumn<ITask>[] = [
    {
      name: t("tasks:columns.name"),
      render: (_value: any, item: ITask) => getTaskMetadata(item.id).name,
    },
    {
      name: t("tasks:columns.description"),
      render: (_value: any, item: ITask) => getTaskMetadata(item.id).description,
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
    </PageTemplate>
  );
};
