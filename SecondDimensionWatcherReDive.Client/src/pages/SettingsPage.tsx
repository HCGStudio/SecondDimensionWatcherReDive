import {
  AlertTriangle,
  Copy,
  KeyRound,
  Plus,
  ShieldAlert,
  Trash2,
} from "lucide-react";
import React from "react";
import { useTranslation } from "react-i18next";

import { useToast } from "../components/ToastProvider";
import { Button } from "../components/ui/Button";
import { Card } from "../components/ui/Card";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { FormRow } from "../components/ui/FormRow";
import { Input } from "../components/ui/Input";
import { Table, type TableColumn } from "../components/ui/Table";
import { useWebDavTokens } from "../settings/hooks";
import {
  ICreateWebDavTokenResponse,
  IWebDavToken,
} from "../settings/IWebDavToken";
import { createWebDavToken, deleteWebDavToken } from "../settings/utils";
import { PageTemplate } from "./PageTemplate";

export const SettingsPage: React.FC = () => {
  const { t } = useTranslation(["settings", "errors"]);
  const { data: tokens, error, mutate } = useWebDavTokens();
  const { addToast } = useToast();
  const [username, setUsername] = React.useState("");
  const [description, setDescription] = React.useState("");
  const [pending, setPending] = React.useState(false);
  const [created, setCreated] =
    React.useState<ICreateWebDavTokenResponse | null>(null);

  const onCreate = React.useCallback(async () => {
    if (pending) return;
    setPending(true);
    try {
      const response = await createWebDavToken(
        username.trim() || undefined,
        description.trim() || undefined,
      );
      setCreated(response);
      setUsername("");
      setDescription("");
      await mutate();
      addToast({ title: t("settings:webdav.toast.created"), color: "success" });
    } catch {
      addToast({
        title: t("settings:webdav.toast.createFailed"),
        color: "danger",
      });
    } finally {
      setPending(false);
    }
  }, [username, description, pending, mutate, addToast, t]);

  const onDelete = React.useCallback(
    async (token: IWebDavToken) => {
      if (
        !window.confirm(
          t("settings:webdav.list.deleteConfirm", { username: token.username }),
        )
      )
        return;
      try {
        await deleteWebDavToken(token.id);
        await mutate();
        addToast({
          title: t("settings:webdav.toast.deleted"),
          color: "success",
        });
      } catch {
        addToast({
          title: t("settings:webdav.toast.deleteFailed"),
          color: "danger",
        });
      }
    },
    [mutate, addToast, t],
  );

  const copy = React.useCallback(
    async (value: string) => {
      try {
        await navigator.clipboard.writeText(value);
        addToast({
          title: t("settings:webdav.toast.copied"),
          color: "success",
        });
      } catch {
        addToast({
          title: t("settings:webdav.toast.copyFailed"),
          color: "danger",
        });
      }
    },
    [addToast, t],
  );

  const columns: TableColumn<IWebDavToken>[] = [
    {
      field: "username",
      name: t("settings:webdav.list.columns.username"),
      render: (value: string) => (
        <span className="font-mono text-foreground">{value}</span>
      ),
    },
    {
      field: "description",
      name: t("settings:webdav.list.columns.description"),
      render: (value: string | undefined) => value || "-",
    },
    {
      field: "createdAt",
      name: t("settings:webdav.list.columns.createdAt"),
      render: (value: string) => new Date(value).toLocaleString(),
    },
    {
      name: t("settings:webdav.list.columns.actions"),
      render: (_value: unknown, item: IWebDavToken) => (
        <Button
          variant="icon"
          color="danger"
          size="sm"
          aria-label={t("settings:webdav.list.deleteAria", {
            username: item.username,
          })}
          onClick={() => onDelete(item)}
        >
          <Trash2 size={16} />
        </Button>
      ),
      width: "60px",
    },
  ];

  return (
    <PageTemplate>
      <header className="mb-6">
        <h1 className="font-serif text-2xl font-medium text-foreground">
          {t("settings:webdav.title")}
        </h1>
        <p className="mt-2 max-w-2xl text-sm leading-body text-muted">
          {t("settings:webdav.intro")}
        </p>
      </header>

      <Card
        icon={<KeyRound size={18} />}
        title={t("settings:webdav.create.title")}
      >
        <div className="flex flex-col gap-4 md:flex-row md:items-end">
          <FormRow
            label={t("settings:webdav.create.usernameLabel")}
            className="flex-1"
          >
            <Input
              placeholder={t("settings:webdav.create.usernamePlaceholder")}
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              maxLength={32}
            />
          </FormRow>
          <FormRow
            label={t("settings:webdav.create.descriptionLabel")}
            className="flex-1"
          >
            <Input
              placeholder={t("settings:webdav.create.descriptionPlaceholder")}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              maxLength={120}
            />
          </FormRow>
          <FormRow hasEmptyLabelSpace>
            <Button onClick={onCreate} disabled={pending}>
              <Plus size={16} />
              {t("settings:webdav.create.submit")}
            </Button>
          </FormRow>
        </div>
        <p className="mt-3 text-xs leading-body text-subtle">
          {t("settings:webdav.create.usernameHelp")}
        </p>
      </Card>

      {created ? (
        <div className="mt-6 rounded-md border border-warning/40 bg-warning/10 p-4">
          <div className="flex items-start gap-3">
            <ShieldAlert size={18} className="mt-0.5 shrink-0 text-warning" />
            <div className="min-w-0 flex-1">
              <h3 className="font-serif text-base font-medium text-foreground">
                {t("settings:webdav.created.title")}
              </h3>
              <p className="mt-1 text-sm leading-body text-muted">
                {t("settings:webdav.created.warning")}
              </p>
              <div className="mt-3 space-y-2">
                <CredentialRow
                  label={t("settings:webdav.created.usernameLabel")}
                  value={created.username}
                  onCopy={() => copy(created.username)}
                />
                <CredentialRow
                  label={t("settings:webdav.created.tokenLabel")}
                  value={created.token}
                  onCopy={() => copy(created.token)}
                />
              </div>
              <div className="mt-4">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setCreated(null)}
                >
                  {t("settings:webdav.created.dismiss")}
                </Button>
              </div>
            </div>
          </div>
        </div>
      ) : null}

      <div className="mt-8">
        {error ? (
          <EmptyPrompt
            icon={<AlertTriangle size={48} />}
            title={<h2>{t("errors:loadFailed")}</h2>}
          />
        ) : tokens && tokens.length > 0 ? (
          <Table items={tokens} columns={columns} />
        ) : tokens ? (
          <EmptyPrompt
            title={<h2>{t("settings:webdav.list.empty.title")}</h2>}
            body={<p>{t("settings:webdav.list.empty.body")}</p>}
          />
        ) : null}
      </div>
    </PageTemplate>
  );
};

interface CredentialRowProps {
  label: string;
  value: string;
  onCopy: () => void;
}

const CredentialRow: React.FC<CredentialRowProps> = ({
  label,
  value,
  onCopy,
}) => {
  const { t } = useTranslation("settings");
  return (
    <div className="flex items-center gap-2 rounded-md border border-border-light bg-surface px-3 py-2">
      <div className="min-w-0 flex-1">
        <div className="text-xs uppercase tracking-wide text-subtle">
          {label}
        </div>
        <div
          className="truncate font-mono text-sm text-foreground"
          title={value}
        >
          {value}
        </div>
      </div>
      <Button
        variant="icon"
        size="sm"
        aria-label={t("webdav.toast.copied")}
        onClick={onCopy}
      >
        <Copy size={14} />
      </Button>
    </div>
  );
};
