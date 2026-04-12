import {
  EuiBasicTable,
  EuiButton,
  EuiButtonIcon,
  EuiEmptyPrompt,
  EuiFieldText,
  EuiFlexGroup,
  EuiFlexItem,
  EuiFormRow,
  EuiSpacer,
} from "@elastic/eui";
import React from "react";

import { useFeeds } from "../feed/hooks";
import { addFeed, removeFeed } from "../feed/utils";
import { IFeed } from "../feed/IFeed";
import { useToast } from "../compoments/ToastProvider";
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

  const columns = [
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
      render: (item: IFeed) => (
        <EuiButtonIcon
          iconType="trash"
          color="danger"
          aria-label="删除"
          onClick={() => onRemove(item.id)}
        />
      ),
      width: "60px",
    },
  ];

  return (
    <PageTemplate>
      <EuiFlexGroup>
        <EuiFlexItem>
          <EuiFormRow label="订阅 URL">
            <EuiFieldText
              placeholder="https://mikanani.me/RSS/..."
              value={url}
              onChange={(e) => setUrl(e.target.value)}
            />
          </EuiFormRow>
        </EuiFlexItem>
        <EuiFlexItem grow={false}>
          <EuiFormRow label="名称（可选）">
            <EuiFieldText
              placeholder="我的订阅"
              value={name}
              onChange={(e) => setName(e.target.value)}
            />
          </EuiFormRow>
        </EuiFlexItem>
        <EuiFlexItem grow={false}>
          <EuiFormRow hasEmptyLabelSpace>
            <EuiButton iconType="plus" onClick={onAdd} fill>
              添加
            </EuiButton>
          </EuiFormRow>
        </EuiFlexItem>
      </EuiFlexGroup>
      <EuiSpacer size="l" />
      {error ? (
        <EuiEmptyPrompt
          iconType="warning"
          title={<h2>加载失败</h2>}
          body={<p>无法获取订阅列表，请稍后重试</p>}
        />
      ) : feeds && feeds.length > 0 ? (
        <EuiBasicTable items={feeds} columns={columns} />
      ) : feeds ? (
        <EuiEmptyPrompt title={<h2>暂无订阅</h2>} body={<p>添加 RSS 订阅以自动获取动画更新</p>} />
      ) : null}
    </PageTemplate>
  );
};
