import dayjs from "dayjs";
import {
  AlertTriangle,
  ChevronRight,
  Download,
  File,
  Folder,
  Home,
} from "lucide-react";
import React from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";

import { useToast } from "../components/ToastProvider";
import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Spinner } from "../components/ui/Spinner";
import { IVfsEntry } from "../file/IVfsEntry";
import { downloadVfsFile, useVfsList } from "../file/vfsHooks";
import { cn } from "../lib/cn";
import { formatFileSize } from "../utils/formatBytes";
import { PageTemplate } from "./PageTemplate";

const ROOT_PATH = "/";

function normalizePath(raw: string | null | undefined): string {
  if (!raw) return ROOT_PATH;
  if (!raw.startsWith("/")) return ROOT_PATH;
  const trimmed = raw.replace(/\/+$/, "");
  return trimmed.length === 0 ? ROOT_PATH : trimmed;
}

function joinPath(base: string, segment: string): string {
  if (base === ROOT_PATH) return `/${segment}`;
  return `${base}/${segment}`;
}

function pathToSegments(path: string): string[] {
  if (path === ROOT_PATH) return [];
  return path.split("/").filter((s) => s.length > 0);
}

function segmentsToPath(segments: string[]): string {
  if (segments.length === 0) return ROOT_PATH;
  return `/${segments.join("/")}`;
}

function sortEntries(entries: IVfsEntry[]): IVfsEntry[] {
  return [...entries].sort((a, b) => {
    if (a.isDirectory !== b.isDirectory) return a.isDirectory ? -1 : 1;
    return a.name.localeCompare(b.name, undefined, {
      numeric: true,
      sensitivity: "base",
    });
  });
}

interface BreadcrumbProps {
  path: string;
  onNavigate: (path: string) => void;
}

const Breadcrumb: React.FC<BreadcrumbProps> = ({ path, onNavigate }) => {
  const { t } = useTranslation("files");
  const segments = pathToSegments(path);

  return (
    <nav
      aria-label={t("vfs.breadcrumb.aria")}
      className="flex flex-wrap items-center gap-1 text-sm"
    >
      <BreadcrumbButton
        active={segments.length === 0}
        onClick={() => onNavigate(ROOT_PATH)}
      >
        <Home size={14} className="shrink-0" />
        {t("vfs.breadcrumb.root")}
      </BreadcrumbButton>
      {segments.map((seg, i) => {
        const isLast = i === segments.length - 1;
        const targetPath = segmentsToPath(segments.slice(0, i + 1));
        return (
          <React.Fragment key={`${i}-${seg}`}>
            <ChevronRight size={14} className="shrink-0 text-subtle" />
            <BreadcrumbButton
              active={isLast}
              onClick={() => onNavigate(targetPath)}
            >
              {seg}
            </BreadcrumbButton>
          </React.Fragment>
        );
      })}
    </nav>
  );
};

interface BreadcrumbButtonProps extends React.PropsWithChildren {
  active: boolean;
  onClick: () => void;
}

const BreadcrumbButton: React.FC<BreadcrumbButtonProps> = ({
  active,
  onClick,
  children,
}) => (
  <button
    type="button"
    onClick={active ? undefined : onClick}
    disabled={active}
    className={cn(
      "inline-flex items-center gap-1.5 rounded-md px-2 py-1 transition-colors",
      active
        ? "cursor-default text-foreground font-medium"
        : "text-muted hover:text-foreground hover:bg-canvas",
    )}
  >
    {children}
  </button>
);

interface DirectoryRowProps {
  entry: IVfsEntry;
  onOpen: () => void;
}

const DirectoryRow: React.FC<DirectoryRowProps> = ({ entry, onOpen }) => {
  return (
    <button
      type="button"
      onClick={onOpen}
      className="group flex w-full items-center gap-3 px-4 py-3 text-left transition-colors hover:bg-canvas focus:outline-hidden focus-visible:bg-canvas"
    >
      <span className="text-muted group-hover:text-brand">
        <Folder size={18} />
      </span>
      <span className="flex-1 truncate font-sans text-sm text-foreground">
        {entry.name}
      </span>
      <ChevronRight size={16} className="text-subtle" />
    </button>
  );
};

interface FileRowProps {
  entry: IVfsEntry;
  fullPath: string;
  onDownloadError: () => void;
}

const FileRow: React.FC<FileRowProps> = ({
  entry,
  fullPath,
  onDownloadError,
}) => {
  const { t } = useTranslation("files");
  const [busy, setBusy] = React.useState(false);

  const onClick = React.useCallback(async () => {
    setBusy(true);
    try {
      await downloadVfsFile(fullPath, entry.name);
    } catch {
      onDownloadError();
    } finally {
      setBusy(false);
    }
  }, [entry.name, fullPath, onDownloadError]);

  const sizeText =
    typeof entry.size === "number" ? formatFileSize(entry.size) : null;
  const modifiedText = entry.lastModifiedUtc
    ? dayjs(entry.lastModifiedUtc).fromNow()
    : null;

  return (
    <div className="flex items-center gap-3 px-4 py-3">
      <span className="text-muted">
        <File size={18} />
      </span>
      <span
        className="flex-1 truncate font-sans text-sm text-foreground"
        title={entry.name}
      >
        {entry.name}
      </span>
      <div className="hidden shrink-0 items-baseline gap-3 text-xs text-subtle sm:flex">
        {sizeText ? <span>{sizeText}</span> : null}
        {modifiedText ? (
          <span title={entry.lastModifiedUtc ?? undefined}>{modifiedText}</span>
        ) : null}
      </div>
      <Button
        variant="icon"
        size="sm"
        aria-label={t("vfs.actions.download")}
        title={t("vfs.actions.download")}
        onClick={onClick}
        disabled={busy}
      >
        {busy ? <Spinner size={16} /> : <Download size={16} />}
      </Button>
    </div>
  );
};

export const FilesPage: React.FC = () => {
  const { t } = useTranslation(["files", "errors"]);
  const [searchParams, setSearchParams] = useSearchParams();
  const path = normalizePath(searchParams.get("path"));
  const { data, error, isLoading } = useVfsList(path);
  const { addToast } = useToast();

  const onNavigate = React.useCallback(
    (next: string) => {
      setSearchParams(
        (params) => {
          if (next === ROOT_PATH) params.delete("path");
          else params.set("path", next);
          return params;
        },
        { replace: false },
      );
    },
    [setSearchParams],
  );

  const onDownloadError = React.useCallback(() => {
    addToast({ title: t("files:vfs.download.failed"), color: "danger" });
  }, [addToast, t]);

  const sorted = React.useMemo(
    () => (data ? sortEntries(data) : null),
    [data],
  );

  return (
    <PageTemplate>
      <header className="mb-6">
        <h2 className="font-serif text-xl font-medium leading-heading text-foreground">
          {t("files:vfs.title")}
        </h2>
        <p className="mt-2 max-w-2xl text-sm leading-body text-muted">
          {t("files:vfs.subtitle")}
        </p>
      </header>

      <div className="mb-4">
        <Breadcrumb path={path} onNavigate={onNavigate} />
      </div>

      <section className="overflow-hidden rounded-xl border border-border-light bg-surface shadow-whisper">
        {error ? (
          <EmptyPrompt
            icon={<AlertTriangle size={40} />}
            title={<h3>{t("errors:loadFailed")}</h3>}
            body={<p>{t("files:vfs.errors.loadFailed")}</p>}
          />
        ) : isLoading || !sorted ? (
          <div className="flex justify-center py-16">
            <Spinner />
          </div>
        ) : sorted.length === 0 ? (
          <EmptyPrompt
            title={<h3>{t("files:vfs.empty.title")}</h3>}
            body={<p>{t("files:vfs.empty.body")}</p>}
          />
        ) : (
          <ul className="divide-y divide-border-light">
            {sorted.map((entry) => (
              <li key={entry.name}>
                {entry.isDirectory ? (
                  <DirectoryRow
                    entry={entry}
                    onOpen={() => onNavigate(joinPath(path, entry.name))}
                  />
                ) : (
                  <FileRow
                    entry={entry}
                    fullPath={joinPath(path, entry.name)}
                    onDownloadError={onDownloadError}
                  />
                )}
              </li>
            ))}
          </ul>
        )}
      </section>
    </PageTemplate>
  );
};
