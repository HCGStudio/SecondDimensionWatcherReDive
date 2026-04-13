import { AlertTriangle, Loader2, Play } from "lucide-react";
import React from "react";

import { useToast } from "../components/ToastProvider";
import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Spinner } from "../components/ui/Spinner";
import { Table, type TableColumn } from "../components/ui/Table";
import { useTasks } from "../tasks/hooks";
import { getTaskMetadata } from "../tasks/taskMetadata";
import { runTask } from "../tasks/utils";
import { ITask } from "../tasks/types";
import { PageTemplate } from "./PageTemplate";

function formatInterval(interval: string): string {
  const match = interval.match(/^(\d+)\.?(\d{2}):(\d{2}):(\d{2})$/);
  if (match) {
    const [, days, hours, minutes] = match;
    const parts: string[] = [];
    if (parseInt(days) > 0) parts.push(`${parseInt(days)}天`);
    if (parseInt(hours) > 0) parts.push(`${parseInt(hours)}小时`);
    if (parseInt(minutes) > 0) parts.push(`${parseInt(minutes)}分钟`);
    return parts.join(" ") || "< 1分钟";
  }
  // Try HH:MM:SS format
  const match2 = interval.match(/^(\d{2}):(\d{2}):(\d{2})$/);
  if (match2) {
    const [, hours, minutes] = match2;
    const parts: string[] = [];
    if (parseInt(hours) > 0) parts.push(`${parseInt(hours)}小时`);
    if (parseInt(minutes) > 0) parts.push(`${parseInt(minutes)}分钟`);
    return parts.join(" ") || "< 1分钟";
  }
  return interval;
}

export const TasksPage: React.FC = () => {
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
          title: `任务「${getTaskMetadata(id).name}」执行完成`,
          color: "success",
        });
      } catch {
        addToast({
          title: `任务「${getTaskMetadata(id).name}」执行失败`,
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
    [mutate, addToast],
  );

  const columns: TableColumn<ITask>[] = [
    {
      name: "任务名称",
      render: (_value: any, item: ITask) => getTaskMetadata(item.id).name,
    },
    {
      name: "描述",
      render: (_value: any, item: ITask) => getTaskMetadata(item.id).description,
    },
    {
      field: "interval",
      name: "执行间隔",
      render: (value: string) => formatInterval(value),
    },
    {
      field: "lastRunAt",
      name: "上次执行",
      render: (value: string | null) =>
        value ? new Date(value).toLocaleString() : "-",
    },
    {
      name: "状态",
      render: (_value: any, item: ITask) =>
        item.isRunning || runningTasks.has(item.id) ? (
          <span className="inline-flex items-center gap-1.5 text-sm text-brand">
            <Loader2 size={14} className="animate-spin" />
            运行中
          </span>
        ) : (
          <span className="text-sm text-success">空闲</span>
        ),
      width: "100px",
    },
    {
      name: "操作",
      render: (_value: any, item: ITask) => (
        <Button
          size="sm"
          variant="outline"
          disabled={item.isRunning || runningTasks.has(item.id)}
          onClick={() => onRun(item.id)}
        >
          <Play size={14} />
          立即运行
        </Button>
      ),
      width: "130px",
    },
  ];

  return (
    <PageTemplate>
      <h2 className="mb-6 font-serif text-xl font-medium text-foreground">
        后台任务
      </h2>
      {error ? (
        <EmptyPrompt
          icon={<AlertTriangle size={48} />}
          title={<h2>加载失败</h2>}
          body={<p>无法获取任务列表，请稍后重试</p>}
        />
      ) : !tasks ? (
        <div className="flex justify-center py-8">
          <Spinner />
        </div>
      ) : tasks.length > 0 ? (
        <Table items={tasks} columns={columns} />
      ) : (
        <EmptyPrompt
          title={<h2>暂无后台任务</h2>}
          body={<p>没有已注册的定时任务</p>}
        />
      )}
    </PageTemplate>
  );
};
