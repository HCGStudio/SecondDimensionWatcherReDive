import React from "react";
import { useTranslation } from "react-i18next";

import {
  AlertTriangle,
  Copy,
  FolderOpen,
  KeyRound,
  Loader2,
  Plus,
  RefreshCw,
  ShieldAlert,
  Trash2,
} from "lucide-react";

import { useToast } from "../components/ToastProvider";
import { Button } from "../components/ui/Button";
import { Card } from "../components/ui/Card";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { FormRow } from "../components/ui/FormRow";
import { Input } from "../components/ui/Input";
import { Table, type TableColumn } from "../components/ui/Table";
import {
  createMediaLibrarySource,
  deleteMediaLibrarySource,
  scanMediaLibrarySource,
  updateMediaLibrarySource,
} from "../mediaLibrary/api";
import { useMediaLibrarySources } from "../mediaLibrary/hooks";
import { IMediaLibrarySource } from "../mediaLibrary/types";
import {
  ICreateWebDavTokenResponse,
  IWebDavToken,
} from "../settings/IWebDavToken";
import { useWebDavTokens } from "../settings/hooks";
import { createWebDavToken, deleteWebDavToken } from "../settings/utils";
import { PageTemplate } from "./PageTemplate";

export const SettingsPage: React.FC = () => {
  const { t } = useTranslation(["settings", "errors"]);
  const {
    data: sources,
    error: sourcesError,
    mutate: mutateSources,
  } = useMediaLibrarySources();
  const {
    data: tokens,
    error: tokensError,
    mutate: mutateTokens,
  } = useWebDavTokens();
  const { addToast } = useToast();
  const [libraryPath, setLibraryPath] = React.useState("");
  const [isMonitoring, setIsMonitoring] = React.useState(true);
  const [sourceCreatePending, setSourceCreatePending] = React.useState(false);
  const [sourcePendingIds, setSourcePendingIds] = React.useState<Set<string>>(
    new Set(),
  );
  const [username, setUsername] = React.useState("");
  const [description, setDescription] = React.useState("");
  const [tokenCreatePending, setTokenCreatePending] = React.useState(false);
  const [created, setCreated] =
    React.useState<ICreateWebDavTokenResponse | null>(null);

  const setSourcePending = React.useCallback((id: string, pending: boolean) => {
    setSourcePendingIds((previous) => {
      const next = new Set(previous);
      if (pending) next.add(id);
      else next.delete(id);
      return next;
    });
  }, []);

  const onCreateSource = React.useCallback(async () => {
    const path = libraryPath.trim();
    if (!path) {
      addToast({
        title: t("settings:mediaLibrary.toast.pathRequired"),
        color: "warning",
      });
      return;
    }

    if (sourceCreatePending) return;
    setSourceCreatePending(true);
    try {
      await createMediaLibrarySource({ path, isMonitoring });
      setLibraryPath("");
      await mutateSources();
      addToast({
        title: t("settings:mediaLibrary.toast.created"),
        color: "success",
      });
    } catch {
      addToast({
        title: t("settings:mediaLibrary.toast.createFailed"),
        color: "danger",
      });
    } finally {
      setSourceCreatePending(false);
    }
  }, [
    addToast,
    isMonitoring,
    libraryPath,
    mutateSources,
    sourceCreatePending,
    t,
  ]);

  const onScanSource = React.useCallback(
    async (source: IMediaLibrarySource) => {
      setSourcePending(source.id, true);
      try {
        await scanMediaLibrarySource(source.id);
        await mutateSources();
        addToast({
          title: t("settings:mediaLibrary.toast.scanQueued"),
          color: "success",
        });
      } catch {
        addToast({
          title: t("settings:mediaLibrary.toast.scanFailed"),
          color: "danger",
        });
      } finally {
        setSourcePending(source.id, false);
      }
    },
    [addToast, mutateSources, setSourcePending, t],
  );

  const onToggleMonitoring = React.useCallback(
    async (source: IMediaLibrarySource, enabled: boolean) => {
      setSourcePending(source.id, true);
      try {
        await updateMediaLibrarySource(source.id, {
          isMonitoring: enabled,
        });
        await mutateSources();
        addToast({
          title: t(
            enabled
              ? "settings:mediaLibrary.toast.monitoringEnabled"
              : "settings:mediaLibrary.toast.monitoringDisabled",
          ),
          color: "success",
        });
      } catch {
        addToast({
          title: t("settings:mediaLibrary.toast.updateFailed"),
          color: "danger",
        });
      } finally {
        setSourcePending(source.id, false);
      }
    },
    [addToast, mutateSources, setSourcePending, t],
  );

  const onDeleteSource = React.useCallback(
    async (source: IMediaLibrarySource) => {
      if (
        !window.confirm(
          t("settings:mediaLibrary.list.deleteConfirm", {
            path: source.path,
          }),
        )
      )
        return;

      setSourcePending(source.id, true);
      try {
        await deleteMediaLibrarySource(source.id);
        await mutateSources();
        addToast({
          title: t("settings:mediaLibrary.toast.deleted"),
          color: "success",
        });
      } catch {
        addToast({
          title: t("settings:mediaLibrary.toast.deleteFailed"),
          color: "danger",
        });
      } finally {
        setSourcePending(source.id, false);
      }
    },
    [addToast, mutateSources, setSourcePending, t],
  );

  const onCreate = React.useCallback(async () => {
    if (tokenCreatePending) return;
    setTokenCreatePending(true);
    try {
      const response = await createWebDavToken(
        username.trim() || undefined,
        description.trim() || undefined,
      );
      setCreated(response);
      setUsername("");
      setDescription("");
      await mutateTokens();
      addToast({ title: t("settings:webdav.toast.created"), color: "success" });
    } catch {
      addToast({
        title: t("settings:webdav.toast.createFailed"),
        color: "danger",
      });
    } finally {
      setTokenCreatePending(false);
    }
  }, [username, description, tokenCreatePending, mutateTokens, addToast, t]);

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
        await mutateTokens();
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
    [mutateTokens, addToast, t],
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

  const sourceColumns: TableColumn<IMediaLibrarySource>[] = [
    {
      field: "path",
      name: t("settings:mediaLibrary.list.columns.path"),
      render: (value: string) => (
        <span className="font-mono text-foreground" title={value}>
          {value}
        </span>
      ),
    },
    {
      name: t("settings:mediaLibrary.list.columns.monitoring"),
      render: (_value: unknown, item: IMediaLibrarySource) => {
        const disabled = item.isScanning || sourcePendingIds.has(item.id);
        return (
          <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-muted">
            <input
              type="checkbox"
              className="h-4 w-4 accent-brand"
              checked={item.isMonitoring}
              disabled={disabled}
              aria-label={t("settings:mediaLibrary.list.monitoringAria", {
                path: item.path,
              })}
              onChange={(event) =>
                void onToggleMonitoring(item, event.target.checked)
              }
            />
            {t(
              item.isMonitoring
                ? "settings:mediaLibrary.list.monitoring"
                : "settings:mediaLibrary.list.manual",
            )}
          </label>
        );
      },
      width: "140px",
    },
    {
      field: "lastScanAt",
      name: t("settings:mediaLibrary.list.columns.lastScanAt"),
      render: (value: string | null) =>
        value
          ? new Date(value).toLocaleString()
          : t("settings:mediaLibrary.list.neverScanned"),
      width: "190px",
    },
    {
      name: t("settings:mediaLibrary.list.columns.result"),
      render: (_value: unknown, item: IMediaLibrarySource) => {
        if (item.isScanning) {
          return (
            <span className="inline-flex items-center gap-1.5 text-brand">
              <Loader2 size={14} className="animate-spin" />
              {t("settings:mediaLibrary.list.scanning")}
            </span>
          );
        }
        if (item.lastError) {
          return (
            <span className="text-error" title={item.lastError}>
              {item.lastError}
            </span>
          );
        }
        if (!item.lastScanAt) return "-";
        return t("settings:mediaLibrary.list.scanResult", {
          imported: item.lastImportedCount,
          updated: item.lastUpdatedCount,
          removed: item.lastRemovedCount,
          skipped: item.lastSkippedCount,
        });
      },
    },
    {
      name: t("settings:mediaLibrary.list.columns.actions"),
      render: (_value: unknown, item: IMediaLibrarySource) => {
        const pending = sourcePendingIds.has(item.id);
        return (
          <div className="flex items-center gap-1">
            <Button
              variant="outline"
              size="sm"
              disabled={pending || item.isScanning}
              aria-label={t("settings:mediaLibrary.list.scanAria", {
                path: item.path,
              })}
              onClick={() => void onScanSource(item)}
            >
              <RefreshCw size={14} />
              {t("settings:mediaLibrary.list.scanNow")}
            </Button>
            <Button
              variant="icon"
              color="danger"
              size="sm"
              disabled={pending || item.isScanning}
              aria-label={t("settings:mediaLibrary.list.deleteAria", {
                path: item.path,
              })}
              onClick={() => void onDeleteSource(item)}
            >
              <Trash2 size={16} />
            </Button>
          </div>
        );
      },
      width: "180px",
    },
  ];

  const tokenColumns: TableColumn<IWebDavToken>[] = [
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
      <header className="mb-8">
        <h1 className="font-serif text-2xl font-medium text-foreground">
          {t("settings:pageTitle")}
        </h1>
      </header>

      <section id="media-library">
        <header className="mb-5">
          <h2 className="font-serif text-xl font-medium text-foreground">
            {t("settings:mediaLibrary.title")}
          </h2>
          <p className="mt-2 max-w-3xl text-sm leading-body text-muted">
            {t("settings:mediaLibrary.intro")}
          </p>
        </header>

        <Card
          icon={<FolderOpen size={18} />}
          title={t("settings:mediaLibrary.create.title")}
        >
          <div className="flex flex-col gap-4 lg:flex-row lg:items-end">
            <FormRow
              label={t("settings:mediaLibrary.create.pathLabel")}
              className="min-w-0 flex-1"
            >
              <Input
                placeholder={t("settings:mediaLibrary.create.pathPlaceholder")}
                value={libraryPath}
                onChange={(event) => setLibraryPath(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === "Enter") void onCreateSource();
                }}
                disabled={sourceCreatePending}
              />
            </FormRow>
            <FormRow hasEmptyLabelSpace>
              <label className="flex min-h-9 cursor-pointer items-center gap-2 rounded-md border border-border px-3 py-2 text-sm text-foreground">
                <input
                  type="checkbox"
                  className="h-4 w-4 accent-brand"
                  checked={isMonitoring}
                  onChange={(event) => setIsMonitoring(event.target.checked)}
                  disabled={sourceCreatePending}
                />
                {t("settings:mediaLibrary.create.monitorLabel")}
              </label>
            </FormRow>
            <FormRow hasEmptyLabelSpace>
              <Button
                onClick={() => void onCreateSource()}
                disabled={sourceCreatePending}
              >
                {sourceCreatePending ? (
                  <Loader2 size={16} className="animate-spin" />
                ) : (
                  <Plus size={16} />
                )}
                {t("settings:mediaLibrary.create.submit")}
              </Button>
            </FormRow>
          </div>
          <p className="mt-3 text-xs leading-body text-subtle">
            {t("settings:mediaLibrary.create.pathHelp")}
          </p>
          <div className="mt-4 rounded-md border border-warning/30 bg-warning/10 px-4 py-3 text-sm leading-body text-muted">
            <p>{t("settings:mediaLibrary.safety.noMove")}</p>
            <p className="mt-1">
              {t("settings:mediaLibrary.safety.containerMount")}
            </p>
          </div>
        </Card>

        <div className="mt-6">
          <h3 className="mb-3 font-serif text-lg font-medium text-foreground">
            {t("settings:mediaLibrary.list.title")}
          </h3>
          {sourcesError ? (
            <EmptyPrompt
              icon={<AlertTriangle size={48} />}
              title={<h3>{t("errors:loadFailed")}</h3>}
              body={<p>{t("settings:mediaLibrary.list.loadFailed")}</p>}
            />
          ) : sources && sources.length > 0 ? (
            <Table items={sources} columns={sourceColumns} />
          ) : sources ? (
            <EmptyPrompt
              title={<h3>{t("settings:mediaLibrary.list.empty.title")}</h3>}
              body={<p>{t("settings:mediaLibrary.list.empty.body")}</p>}
            />
          ) : (
            <div className="flex justify-center py-12">
              <Loader2 size={24} className="animate-spin text-brand" />
            </div>
          )}
        </div>
      </section>

      <section className="mt-12">
        <header className="mb-6">
          <h2 className="font-serif text-xl font-medium text-foreground">
            {t("settings:webdav.title")}
          </h2>
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
              <Button onClick={onCreate} disabled={tokenCreatePending}>
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
          {tokensError ? (
            <EmptyPrompt
              icon={<AlertTriangle size={48} />}
              title={<h2>{t("errors:loadFailed")}</h2>}
            />
          ) : tokens && tokens.length > 0 ? (
            <Table items={tokens} columns={tokenColumns} />
          ) : tokens ? (
            <EmptyPrompt
              title={<h2>{t("settings:webdav.list.empty.title")}</h2>}
              body={<p>{t("settings:webdav.list.empty.body")}</p>}
            />
          ) : null}
        </div>
      </section>
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
