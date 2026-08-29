import React from "react";
import { useTranslation } from "react-i18next";

import {
  AlertTriangle,
  FolderOpen,
  Loader2,
  Plus,
  RefreshCw,
  Trash2,
} from "lucide-react";

import {
  createMediaLibrarySource,
  deleteMediaLibrarySource,
  scanMediaLibrarySource,
  updateMediaLibrarySource,
} from "../../mediaLibrary/api";
import { useMediaLibrarySources } from "../../mediaLibrary/hooks";
import { IMediaLibrarySource } from "../../mediaLibrary/types";
import { useToast } from "../ToastProvider";
import { Button } from "../ui/Button";
import { Card } from "../ui/Card";
import { EmptyPrompt } from "../ui/EmptyPrompt";
import { FormRow } from "../ui/FormRow";
import { Input } from "../ui/Input";
import { Table, TableColumn } from "../ui/Table";

export const MediaLibrarySourcesSection: React.FC = () => {
  const { t } = useTranslation(["settings", "errors"]);
  const { data, error, mutate } = useMediaLibrarySources();
  const { addToast } = useToast();
  const [path, setPath] = React.useState("");
  const [isMonitoring, setIsMonitoring] = React.useState(true);
  const [creating, setCreating] = React.useState(false);
  const [pendingIds, setPendingIds] = React.useState<Set<string>>(new Set());

  const setPending = React.useCallback((id: string, pending: boolean) => {
    setPendingIds((previous) => {
      const next = new Set(previous);
      if (pending) next.add(id);
      else next.delete(id);
      return next;
    });
  }, []);

  const create = React.useCallback(async () => {
    const normalized = path.trim();
    if (!normalized) {
      addToast({
        title: t("settings:mediaLibrary.toast.pathRequired"),
        color: "warning",
      });
      return;
    }
    setCreating(true);
    try {
      await createMediaLibrarySource({ path: normalized, isMonitoring });
      setPath("");
      await mutate();
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
      setCreating(false);
    }
  }, [addToast, isMonitoring, mutate, path, t]);

  const scan = React.useCallback(
    async (source: IMediaLibrarySource) => {
      setPending(source.id, true);
      try {
        await scanMediaLibrarySource(source.id);
        await mutate();
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
        setPending(source.id, false);
      }
    },
    [addToast, mutate, setPending, t],
  );

  const toggleMonitoring = React.useCallback(
    async (source: IMediaLibrarySource, enabled: boolean) => {
      setPending(source.id, true);
      try {
        await updateMediaLibrarySource(source.id, { isMonitoring: enabled });
        await mutate();
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
        setPending(source.id, false);
      }
    },
    [addToast, mutate, setPending, t],
  );

  const remove = React.useCallback(
    async (source: IMediaLibrarySource) => {
      if (
        !window.confirm(
          t("settings:mediaLibrary.list.deleteConfirm", {
            path: source.path,
          }),
        )
      )
        return;
      setPending(source.id, true);
      try {
        await deleteMediaLibrarySource(source.id);
        await mutate();
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
        setPending(source.id, false);
      }
    },
    [addToast, mutate, setPending, t],
  );

  const columns: TableColumn<IMediaLibrarySource>[] = [
    {
      field: "path",
      name: t("settings:mediaLibrary.list.columns.path"),
      render: (value: string) => (
        <span className="font-mono text-foreground" title={value}>
          {value}
        </span>
      ),
      mobile: "primary",
    },
    {
      name: t("settings:mediaLibrary.list.columns.monitoring"),
      render: (_value, item) => (
        <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-muted">
          <input
            type="checkbox"
            className="h-4 w-4 accent-brand"
            checked={item.isMonitoring}
            disabled={item.isScanning || pendingIds.has(item.id)}
            aria-label={t("settings:mediaLibrary.list.monitoringAria", {
              path: item.path,
            })}
            onChange={(event) =>
              void toggleMonitoring(item, event.target.checked)
            }
          />
          {t(
            item.isMonitoring
              ? "settings:mediaLibrary.list.monitoring"
              : "settings:mediaLibrary.list.manual",
          )}
        </label>
      ),
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
      mobile: "hidden",
    },
    {
      name: t("settings:mediaLibrary.list.columns.result"),
      render: (_value, item) => {
        if (item.isScanning)
          return (
            <span className="inline-flex items-center gap-1.5 text-brand">
              <Loader2 size={14} className="animate-spin" />
              {t("settings:mediaLibrary.list.scanning")}
            </span>
          );
        if (item.lastError)
          return (
            <span className="text-error" title={item.lastError}>
              {item.lastError}
            </span>
          );
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
      render: (_value, item) => (
        <div className="flex items-center gap-1">
          <Button
            variant="outline"
            size="sm"
            disabled={pendingIds.has(item.id) || item.isScanning}
            aria-label={t("settings:mediaLibrary.list.scanAria", {
              path: item.path,
            })}
            onClick={() => void scan(item)}
          >
            <RefreshCw size={14} />
            {t("settings:mediaLibrary.list.scanNow")}
          </Button>
          <Button
            variant="icon"
            color="danger"
            size="sm"
            disabled={pendingIds.has(item.id) || item.isScanning}
            aria-label={t("settings:mediaLibrary.list.deleteAria", {
              path: item.path,
            })}
            onClick={() => void remove(item)}
          >
            <Trash2 size={16} />
          </Button>
        </div>
      ),
      width: "180px",
    },
  ];

  return (
    <div className="mt-8 border-t border-border pt-8">
      <header className="mb-5">
        <h3 className="font-serif text-lg font-medium text-foreground">
          {t("settings:mediaLibrary.title")}
        </h3>
        <p className="mt-1 max-w-3xl text-sm leading-body text-muted">
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
              value={path}
              disabled={creating}
              placeholder={t("settings:mediaLibrary.create.pathPlaceholder")}
              onChange={(event) => setPath(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === "Enter") void create();
              }}
            />
          </FormRow>
          <FormRow hasEmptyLabelSpace>
            <label className="flex min-h-9 items-center gap-2 rounded-md border border-border px-3 py-2 text-sm text-foreground">
              <input
                type="checkbox"
                className="h-4 w-4 accent-brand"
                checked={isMonitoring}
                disabled={creating}
                onChange={(event) => setIsMonitoring(event.target.checked)}
              />
              {t("settings:mediaLibrary.create.monitorLabel")}
            </label>
          </FormRow>
          <FormRow hasEmptyLabelSpace>
            <Button disabled={creating} onClick={() => void create()}>
              {creating ? (
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
        {error ? (
          <EmptyPrompt
            role="alert"
            icon={<AlertTriangle size={44} />}
            title={<h3>{t("errors:loadFailed")}</h3>}
            body={<p>{t("settings:mediaLibrary.list.loadFailed")}</p>}
          />
        ) : data && data.length > 0 ? (
          <Table
            items={data}
            columns={columns}
            label={t("settings:mediaLibrary.list.title")}
            rowKey={(source) => source.id}
          />
        ) : data ? (
          <EmptyPrompt
            title={<h3>{t("settings:mediaLibrary.list.empty.title")}</h3>}
            body={<p>{t("settings:mediaLibrary.list.empty.body")}</p>}
          />
        ) : (
          <div className="flex justify-center py-10">
            <Loader2 size={22} className="animate-spin text-brand" />
          </div>
        )}
      </div>
    </div>
  );
};
