import React from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { mutate as mutateCache } from "swr";

import {
  CheckCircle2,
  Circle,
  CornerDownLeft,
  File,
  FolderOpen,
  Play,
} from "lucide-react";

import { IFileStoreListResult } from "../file/IFileStoreListResult";
import { useFileList } from "../file/hooks";
import { setPlaybackWatched } from "../playback/api";
import { usePlaybackStates } from "../playback/hooks";
import { useToast } from "./ToastProvider";
import { Button } from "./ui/Button";
import { Spinner } from "./ui/Spinner";
import { Table, type TableColumn } from "./ui/Table";

interface FileBrowserProps {
  animationId: string;
}

export const FileBrowser: React.FC<FileBrowserProps> = ({ animationId }) => {
  const { t } = useTranslation("files");
  const [relativeDir, setRelativeDir] = React.useState<string | undefined>(
    undefined,
  );
  const { data: files, error } = useFileList(animationId, relativeDir);
  const { data: playbackStates, mutate: mutatePlaybackStates } =
    usePlaybackStates(animationId);
  const navigate = useNavigate();
  const { addToast } = useToast();
  const [updatingPath, setUpdatingPath] = React.useState<string | null>(null);

  const stateByPath = React.useMemo(
    () => new Map(playbackStates?.map((state) => [state.path, state]) ?? []),
    [playbackStates],
  );

  const onPlay = React.useCallback(
    (path?: string) => {
      const params = new URLSearchParams();
      if (path) params.set("file", path);
      navigate(`/play/${animationId}?${params.toString()}`);
    },
    [animationId, navigate],
  );

  const onNavigate = React.useCallback((dir: string | null) => {
    if (dir) {
      setRelativeDir((prev) => (prev ? `${prev}/${dir}` : dir));
    }
  }, []);

  const onGoUp = React.useCallback(() => {
    setRelativeDir((prev) => {
      if (!prev) return undefined;
      const parts = prev.split("/");
      parts.pop();
      return parts.length > 0 ? parts.join("/") : undefined;
    });
  }, []);

  const onToggleWatched = React.useCallback(
    async (path: string) => {
      const state = stateByPath.get(path);
      if (!state) return;
      setUpdatingPath(path);
      try {
        await setPlaybackWatched({
          animationInfoId: animationId,
          path,
          isWatched: !state.isWatched,
        });
        await mutatePlaybackStates();
        await mutateCache(
          (key) =>
            typeof key === "string" &&
            key.startsWith("/api/playback/continue?"),
        );
      } catch {
        addToast({ title: t("browser.watchStateFailed"), color: "danger" });
      } finally {
        setUpdatingPath(null);
      }
    },
    [addToast, animationId, mutatePlaybackStates, stateByPath, t],
  );

  if (error) {
    return <p className="text-sm text-error">{t("browser.loadFailed")}</p>;
  }

  if (!files) {
    return <Spinner size={24} />;
  }

  const columns: TableColumn<IFileStoreListResult>[] = [
    {
      field: "fileName",
      name: t("browser.filename"),
      render: (value: string, item: IFileStoreListResult) => (
        <div className="flex items-center gap-2">
          <span className="shrink-0 text-muted">
            {item.isDirectory ? <FolderOpen size={16} /> : <File size={16} />}
          </span>
          {item.isDirectory ? (
            <Button
              variant="ghost"
              size="sm"
              onClick={() => onNavigate(item.relative)}
            >
              {value}
            </Button>
          ) : (
            <span className="text-sm">{value}</span>
          )}
        </div>
      ),
    },
    {
      name: t("browser.actions"),
      render: (_value: any, item: IFileStoreListResult) =>
        !item.isDirectory
          ? (() => {
              const path = relativeDir
                ? `${relativeDir}/${item.fileName}`
                : item.fileName;
              const state = stateByPath.get(path);
              return (
                <div className="flex items-center justify-end gap-1">
                  {state ? (
                    <Button
                      variant="icon"
                      size="sm"
                      color={state.isWatched ? "success" : "default"}
                      disabled={updatingPath === path}
                      aria-label={t(
                        state.isWatched
                          ? "browser.markUnwatched"
                          : "browser.markWatched",
                      )}
                      title={t(
                        state.isWatched
                          ? "browser.markUnwatched"
                          : "browser.markWatched",
                      )}
                      onClick={() => void onToggleWatched(path)}
                    >
                      {state.isWatched ? (
                        <CheckCircle2 size={16} />
                      ) : (
                        <Circle size={16} />
                      )}
                    </Button>
                  ) : null}
                  {state ? (
                    <Button
                      variant="icon"
                      size="sm"
                      aria-label={t("browser.play")}
                      onClick={() => onPlay(path)}
                    >
                      <Play size={16} />
                    </Button>
                  ) : null}
                </div>
              );
            })()
          : null,
      width: "96px",
    },
  ];

  return (
    <>
      {relativeDir ? (
        <Button variant="ghost" size="sm" onClick={onGoUp} className="mb-2">
          <CornerDownLeft size={16} />
          {t("browser.goUp")}
        </Button>
      ) : null}
      <Table items={files} columns={columns} />
    </>
  );
};
