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

import { useUsers } from "../../accounts/hooks";
import { retryAfterReauthentication } from "../../auth/utils";
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
  const { data: users } = useUsers(true);
  const { addToast } = useToast();
  const [username, setUsername] = React.useState("");
  const [description, setDescription] = React.useState("");
  const [virtualRoot, setVirtualRoot] = React.useState("/");
  const [expiresAt, setExpiresAt] = React.useState("");
  const [userId, setUserId] = React.useState("");
  const [creating, setCreating] = React.useState(false);
  const [created, setCreated] =
    React.useState<ICreateWebDavTokenResponse | null>(null);

  const create = React.useCallback(async () => {
    if (creating) return;
    setCreating(true);
    try {
      const response = await retryAfterReauthentication(
        () =>
          createWebDavToken(
            username.trim() || undefined,
            description.trim() || undefined,
            virtualRoot.trim() || "/",
            expiresAt
              ? new Date(`${expiresAt}T23:59:59`).toISOString()
              : undefined,
            userId || undefined,
          ),
        t("settings:system.reauthenticatePrompt"),
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
  }, [
    addToast,
    creating,
    description,
    expiresAt,
    mutate,
    t,
    username,
    userId,
    virtualRoot,
  ]);

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
        await retryAfterReauthentication(
          () => deleteWebDavToken(token.id),
          t("settings:system.reauthenticatePrompt"),
        );
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
    },
    {
      field: "description",
      name: t("settings:webdav.list.columns.description"),
      render: (value: string | undefined) => value || "-",
    },
    {
      field: "userId",
      name: t("settings:webdav.list.columns.user"),
      render: (value: string) =>
        users?.find((user) => user.id === value)?.username ?? value,
    },
    {
      field: "virtualRoot",
      name: t("settings:webdav.list.columns.virtualRoot"),
      render: (value: string) => (
        <span className="font-mono text-foreground">{value}</span>
      ),
    },
    {
      field: "expiresAt",
      name: t("settings:webdav.list.columns.expiresAt"),
      render: (value: string | undefined, item) =>
        item.revokedAt
          ? t("settings:webdav.list.revoked")
          : value
            ? new Date(value).toLocaleString()
            : "-",
    },
    {
      field: "createdAt",
      name: t("settings:webdav.list.columns.createdAt"),
      render: (value: string) => new Date(value).toLocaleString(),
    },
    {
      name: t("settings:webdav.list.columns.actions"),
      render: (_value, item) =>
        item.revokedAt ? null : (
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
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3 xl:items-end">
          <FormRow label={t("settings:webdav.create.userLabel")}>
            <select
              className="rounded-lg border border-border bg-surface px-3 py-2 text-sm"
              value={userId}
              onChange={(event) => setUserId(event.target.value)}
            >
              <option value="">
                {t("settings:webdav.create.currentUser")}
              </option>
              {users?.map((user) => (
                <option key={user.id} value={user.id}>
                  {user.username}
                </option>
              ))}
            </select>
          </FormRow>
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
          <FormRow label={t("settings:webdav.create.virtualRootLabel")}>
            <Input
              value={virtualRoot}
              placeholder="/"
              onChange={(event) => setVirtualRoot(event.target.value)}
            />
          </FormRow>
          <FormRow label={t("settings:webdav.create.expiresAtLabel")}>
            <Input
              type="date"
              value={expiresAt}
              onChange={(event) => setExpiresAt(event.target.value)}
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
                  onCopy={() => void copy(created.username)}
                />
                <CredentialRow
                  label={t("settings:webdav.created.tokenLabel")}
                  value={created.token}
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
            icon={<AlertTriangle size={44} />}
            title={<h3>{t("errors:loadFailed")}</h3>}
          />
        ) : data && data.length > 0 ? (
          <Table items={data} columns={columns} />
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
  onCopy: () => void;
}

const CredentialRow: React.FC<CredentialRowProps> = ({
  label,
  value,
  onCopy,
}) => (
  <div className="flex items-center gap-2 rounded-md border border-border-light bg-surface px-3 py-2">
    <div className="min-w-0 flex-1">
      <div className="text-xs uppercase tracking-wide text-subtle">{label}</div>
      <div className="truncate font-mono text-sm text-foreground" title={value}>
        {value}
      </div>
    </div>
    <Button variant="icon" size="sm" onClick={onCopy}>
      <Copy size={14} />
    </Button>
  </div>
);
