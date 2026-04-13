import { CornerDownLeft, File, FolderOpen, Play } from "lucide-react";
import React from "react";
import { useNavigate } from "react-router-dom";

import { useFileList } from "../file/hooks";
import { IFileStoreListResult } from "../file/IFileStoreListResult";
import { Button } from "./ui/Button";
import { Spinner } from "./ui/Spinner";
import { Table, type TableColumn } from "./ui/Table";

interface FileBrowserProps {
  animationId: string;
}

export const FileBrowser: React.FC<FileBrowserProps> = ({ animationId }) => {
  const [relativeDir, setRelativeDir] = React.useState<string | undefined>(
    undefined,
  );
  const { data: files, error } = useFileList(animationId, relativeDir);
  const navigate = useNavigate();

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

  if (error) {
    return (
      <p className="text-sm text-error">加载文件列表失败</p>
    );
  }

  if (!files) {
    return <Spinner size={24} />;
  }

  const columns: TableColumn<IFileStoreListResult>[] = [
    {
      field: "fileName",
      name: "文件名",
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
      name: "操作",
      render: (_value: any, item: IFileStoreListResult) =>
        !item.isDirectory ? (
          <Button
            variant="icon"
            size="sm"
            aria-label="播放"
            onClick={() =>
              onPlay(
                relativeDir
                  ? `${relativeDir}/${item.fileName}`
                  : item.fileName,
              )
            }
          >
            <Play size={16} />
          </Button>
        ) : null,
      width: "60px",
    },
  ];

  return (
    <>
      {relativeDir ? (
        <Button variant="ghost" size="sm" onClick={onGoUp} className="mb-2">
          <CornerDownLeft size={16} />
          返回上级
        </Button>
      ) : null}
      <Table items={files} columns={columns} />
    </>
  );
};
