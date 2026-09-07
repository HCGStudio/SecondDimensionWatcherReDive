import React from "react";
import { useTranslation } from "react-i18next";

import {
  AlertTriangle,
  Copy,
  KeyRound,
  Plus,
  ShieldAlert,
  Trash2,
} from "lucide-react";

import {
  ICreateWebDavTokenResponse,
  IWebDavToken,
} from "../../settings/IWebDavToken";
import { useWebDavTokens } from "../../settings/hooks";
import { createWebDavToken, deleteWebDavToken } from "../../settings/utils";
import { useToast } from "../ToastProvider";
import { Button } from "../ui/Button";
import { Card } from "../ui/Card";
import { EmptyPrompt } from "../ui/EmptyPrompt";
import { FormRow } from "../ui/FormRow";
import { Input } from "../ui/Input";
import { Table, TableColumn } from "../ui/Table";

export const WebDavSettingsSection: React.FC = () => {
  const { t } = useTranslation(["settings", "errors"]);
  const { data, error, mutate } = useWebDavTokens();
  const { addToast } = useToast();
  const [username, setUsername] = React.useState("");
  const [description, setDescription] = React.useState("");
  const [creating, setCreating] = React.useState(false);
  const [created, setCreated] =
    React.useState<ICreateWebDavTokenResponse | null>(null);

  const create = React.useCallback(async () => {
    if (creating) return;
    setCreating(true);
    try {
      const response = await createWebDavToken(
        username.trim() || undefined,
        description.trim() || undefined,
      );
      setCreated(response);
      setUsername("");
      setDescription("");
      await mutate();
      addToast({
        title: t("settings:webdav.toast.created"),
        color: "success",
      });
    } catch {
      addToast({
        title: t("settings:webdav.toast.createFailed"),
        color: "danger",
      });
    } finally {
      setCreating(false);
    }
  }, [addToast, creating, description, mutate, t, username]);

  const remove = React.useCallback(
    async (token: IWebDavToken) => {
      if (
        !window.confirm(
          t("settings:webdav.list.deleteConfirm", {
            username: token.username,
          }),
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
    [addToast, mutate, t],
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
      mobile: "primary",
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
      mobile: "hidden",
    },
    {
      name: t("settings:webdav.list.columns.actions"),
      render: (_value, item) => (
        <Button
          variant="icon"
          color="danger"
          size="sm"
          aria-label={t("settings:webdav.list.deleteAria", {
            username: item.username,
          })}
          onClick={() => void remove(item)}
        >
          <Trash2 size={16} />
        </Button>
      ),
      width: "60px",
    },
  ];

  return (
    <div className="mt-8 border-t border-border pt-8">
      <header className="mb-5">
        <h3 className="font-serif text-lg font-medium text-foreground">
          {t("settings:webdav.title")}
        </h3>
        <p className="mt-1 max-w-3xl text-sm leading-body text-muted">
          {t("settings:webdav.intro")}
        </p>
      </header>
      <Card
        icon={<KeyRound size={18} />}
        title={t("settings:webdav.create.title")}
      >
        <div className="flex flex-col gap-4 md:flex-row md:items-end">
          <FormRow
            className="flex-1"
            label={t("settings:webdav.create.usernameLabel")}
          >
            <Input
              value={username}
              maxLength={32}
              placeholder={t("settings:webdav.create.usernamePlaceholder")}
              onChange={(event) => setUsername(event.target.value)}
            />
          </FormRow>
          <FormRow
            className="flex-1"
            label={t("settings:webdav.create.descriptionLabel")}
          >
            <Input
              value={description}
              maxLength={120}
              placeholder={t("settings:webdav.create.descriptionPlaceholder")}
              onChange={(event) => setDescription(event.target.value)}
            />
          </FormRow>
          <FormRow hasEmptyLabelSpace>
            <Button disabled={creating} onClick={() => void create()}>
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
        <div className="mt-5 rounded-lg border border-warning/40 bg-warning/10 p-4">
          <div className="flex items-start gap-3">
            <ShieldAlert size={18} className="mt-0.5 shrink-0 text-warning" />
            <div className="min-w-0 flex-1">
              <h4 className="font-serif text-base font-medium text-foreground">
                {t("settings:webdav.created.title")}
              </h4>
              <p className="mt-1 text-sm leading-body text-muted">
                {t("settings:webdav.created.warning")}
              </p>
              <div className="mt-3 space-y-2">
                <CredentialRow
                  label={t("settings:webdav.created.usernameLabel")}
                  value={created.username}
                  copyLabel={t("settings:webdav.created.copyAria", {
                    label: t("settings:webdav.created.usernameLabel"),
                  })}
                  onCopy={() => void copy(created.username)}
                />
                <CredentialRow
                  label={t("settings:webdav.created.tokenLabel")}
                  value={created.token}
                  copyLabel={t("settings:webdav.created.copyAria", {
                    label: t("settings:webdav.created.tokenLabel"),
                  })}
                  onCopy={() => void copy(created.token)}
                />
              </div>
              <Button
                className="mt-4"
                variant="outline"
                size="sm"
                onClick={() => setCreated(null)}
              >
                {t("settings:webdav.created.dismiss")}
              </Button>
            </div>
          </div>
        </div>
      ) : null}

      <div className="mt-6">
        {error ? (
          <EmptyPrompt
            role="alert"
            icon={<AlertTriangle size={44} />}
            title={<h3>{t("errors:loadFailed")}</h3>}
          />
        ) : data && data.length > 0 ? (
          <Table
            items={data}
            columns={columns}
            label={t("settings:webdav.title")}
            rowKey={(token) => token.id}
          />
        ) : data ? (
          <EmptyPrompt
            title={<h3>{t("settings:webdav.list.empty.title")}</h3>}
            body={<p>{t("settings:webdav.list.empty.body")}</p>}
          />
        ) : null}
      </div>
    </div>
  );
};

interface CredentialRowProps {
  label: string;
  value: string;
  copyLabel: string;
  onCopy: () => void;
}

const CredentialRow: React.FC<CredentialRowProps> = ({
  label,
  value,
  copyLabel,
  onCopy,
}) => (
  <div className="flex items-center gap-2 rounded-md border border-border-light bg-surface px-3 py-2">
    <div className="min-w-0 flex-1">
      <div className="text-xs uppercase tracking-wide text-subtle">{label}</div>
      <div className="truncate font-mono text-sm text-foreground" title={value}>
        {value}
      </div>
    </div>
    <Button
      variant="icon"
      size="sm"
      aria-label={copyLabel}
      title={copyLabel}
      onClick={onCopy}
    >
      <Copy size={14} />
    </Button>
  </div>
);
