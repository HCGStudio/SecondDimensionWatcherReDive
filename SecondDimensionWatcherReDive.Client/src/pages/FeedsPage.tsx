import { AlertTriangle, Plus, Trash2 } from "lucide-react";
import React from "react";
import { useTranslation } from "react-i18next";

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
  const { t } = useTranslation(["feeds", "errors"]);
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
      addToast({ title: t("feeds:toast.added"), color: "success" });
    } catch {
      addToast({ title: t("feeds:toast.addFailed"), color: "danger" });
    }
  }, [url, name, mutate, addToast, t]);

  const onRemove = React.useCallback(
    async (id: string) => {
      try {
        await removeFeed(id);
        await mutate();
        addToast({ title: t("feeds:toast.deleted"), color: "success" });
      } catch {
        addToast({ title: t("feeds:toast.deleteFailed"), color: "danger" });
      }
    },
    [mutate, addToast, t],
  );

  const columns: TableColumn<IFeed>[] = [
    {
      field: "name",
      name: t("feeds:columns.name"),
      render: (value: string | undefined) => value || "-",
    },
    {
      field: "url",
      name: t("feeds:columns.url"),
      truncateText: true,
    },
    {
      field: "createdAt",
      name: t("feeds:columns.createdAt"),
      render: (value: string) => new Date(value).toLocaleString(),
    },
    {
      name: t("feeds:columns.actions"),
      render: (_value: any, item: IFeed) => (
        <Button
          variant="icon"
          color="danger"
          size="sm"
          aria-label={t("feeds:columns.delete")}
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
      <h2 className="mb-4 font-serif text-xl font-medium text-foreground">
        {t("feeds:manualSubscribe")}
      </h2>
      <div className="flex items-end gap-4">
        <FormRow label={t("feeds:urlLabel")} className="flex-1">
          <Input
            placeholder={t("feeds:urlPlaceholder")}
            value={url}
            onChange={(e) => setUrl(e.target.value)}
          />
        </FormRow>
        <FormRow label={t("feeds:nameLabel")}>
          <Input
            placeholder={t("feeds:namePlaceholder")}
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
        </FormRow>
        <FormRow hasEmptyLabelSpace>
          <Button onClick={onAdd}>
            <Plus size={16} />
            {t("feeds:add")}
          </Button>
        </FormRow>
      </div>
      <div className="mt-8">
        {error ? (
          <EmptyPrompt
            icon={<AlertTriangle size={48} />}
            title={<h2>{t("errors:loadFailed")}</h2>}
            body={<p>{t("feeds:loadFailed")}</p>}
          />
        ) : feeds && feeds.length > 0 ? (
          <Table items={feeds} columns={columns} />
        ) : feeds ? (
          <EmptyPrompt
            title={<h2>{t("feeds:empty.title")}</h2>}
            body={<p>{t("feeds:empty.body")}</p>}
          />
        ) : null}
      </div>
    </PageTemplate>
  );
};
