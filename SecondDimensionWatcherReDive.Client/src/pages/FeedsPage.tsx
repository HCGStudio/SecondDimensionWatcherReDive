import { AlertTriangle, Plus, Trash2 } from "lucide-react";
import React from "react";

import { useFeeds } from "../feed/hooks";
import { addFeed, removeFeed } from "../feed/utils";
import { IFeed } from "../feed/IFeed";
import { SeasonDiscovery } from "../season/SeasonDiscovery";
import { useToast } from "../components/ToastProvider";
import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { FormRow } from "../components/ui/FormRow";
import { Input } from "../components/ui/Input";
import { Table, type TableColumn } from "../components/ui/Table";
import { PageTemplate } from "./PageTemplate";

export const FeedsPage: React.FC = () => {
  const { data: feeds, error, mutate } = useFeeds();
  const { addToast } = useToast();
  const [url, setUrl] = React.useState("");
  const [name, setName] = React.useState("");

  const onAdd = React.useCallback(async () => {
    if (!url.trim()) return;
    try {
      await addFeed(url.trim(), name.trim() || undefined);
      setUrl("");
      setName("");
      await mutate();
      addToast({ title: "订阅添加成功", color: "success" });
    } catch {
      addToast({ title: "添加订阅失败", color: "danger" });
    }
  }, [url, name, mutate, addToast]);

  const onRemove = React.useCallback(
    async (id: string) => {
      try {
        await removeFeed(id);
        await mutate();
        addToast({ title: "订阅已删除", color: "success" });
      } catch {
        addToast({ title: "删除订阅失败", color: "danger" });
      }
    },
    [mutate, addToast],
  );

  const columns: TableColumn<IFeed>[] = [
    {
      field: "name",
      name: "名称",
      render: (value: string | undefined) => value || "-",
    },
    {
      field: "url",
      name: "URL",
      truncateText: true,
    },
    {
      field: "createdAt",
      name: "添加时间",
      render: (value: string) => new Date(value).toLocaleString(),
    },
    {
      name: "操作",
      render: (_value: any, item: IFeed) => (
        <Button
          variant="icon"
          color="danger"
          size="sm"
          aria-label="删除"
          onClick={() => onRemove(item.id)}
        >
          <Trash2 size={16} />
        </Button>
      ),
      width: "60px",
    },
  ];

  return (
    <PageTemplate>
      <SeasonDiscovery />
      <hr className="my-8 border-border-light" />
      <h2 className="mb-4 font-serif text-xl font-medium text-foreground">手动订阅</h2>
      <div className="flex items-end gap-4">
        <FormRow label="订阅 URL" className="flex-1">
          <Input
            placeholder="https://mikanani.me/RSS/..."
            value={url}
            onChange={(e) => setUrl(e.target.value)}
          />
        </FormRow>
        <FormRow label="名称（可选）">
          <Input
            placeholder="我的订阅"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
        </FormRow>
        <FormRow hasEmptyLabelSpace>
          <Button onClick={onAdd}>
            <Plus size={16} />
            添加
          </Button>
        </FormRow>
      </div>
      <div className="mt-8">
        {error ? (
          <EmptyPrompt
            icon={<AlertTriangle size={48} />}
            title={<h2>加载失败</h2>}
            body={<p>无法获取订阅列表，请稍后重试</p>}
          />
        ) : feeds && feeds.length > 0 ? (
          <Table items={feeds} columns={columns} />
        ) : feeds ? (
          <EmptyPrompt
            title={<h2>暂无订阅</h2>}
            body={<p>添加 RSS 订阅以自动获取动画更新</p>}
          />
        ) : null}
      </div>
    </PageTemplate>
  );
};
