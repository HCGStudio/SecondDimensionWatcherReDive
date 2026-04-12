import {
  EuiBasicTable,
  EuiButtonEmpty,
  EuiButtonIcon,
  EuiFlexGroup,
  EuiFlexItem,
  EuiIcon,
  EuiLoadingSpinner,
  EuiText,
} from "@elastic/eui";
import React from "react";

import { useFileList } from "../file/hooks";
import { generatePlaybackLink } from "../file/utils";
import { IFileStoreListResult } from "../file/IFileStoreListResult";
import { useToast } from "./ToastProvider";

interface FileBrowserProps {
  animationId: string;
}

export const FileBrowser: React.FC<FileBrowserProps> = ({ animationId }) => {
  const [relativeDir, setRelativeDir] = React.useState<string | undefined>(
    undefined,
  );
  const { data: files, error } = useFileList(animationId, relativeDir);
  const { addToast } = useToast();

  const onPlay = React.useCallback(
    async (path?: string) => {
      try {
        const result = await generatePlaybackLink(animationId, path);
        window.open(result.url, "_blank");
      } catch {
        addToast({ title: "生成播放链接失败", color: "danger" });
      }
    },
    [animationId, addToast],
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
      <EuiText color="danger" size="s">
        加载文件列表失败
      </EuiText>
    );
  }

  if (!files) {
    return <EuiLoadingSpinner size="m" />;
  }

  const columns = [
    {
      field: "fileName",
      name: "文件名",
      render: (value: string, item: IFileStoreListResult) => (
        <EuiFlexGroup alignItems="center" gutterSize="s" responsive={false}>
          <EuiFlexItem grow={false}>
            <EuiIcon type={item.isDirectory ? "folderOpen" : "document"} />
          </EuiFlexItem>
          <EuiFlexItem>
            {item.isDirectory ? (
              <EuiButtonEmpty
                size="s"
                onClick={() => onNavigate(item.relative)}
              >
                {value}
              </EuiButtonEmpty>
            ) : (
              <EuiText size="s">{value}</EuiText>
            )}
          </EuiFlexItem>
        </EuiFlexGroup>
      ),
    },
    {
      name: "操作",
      render: (item: IFileStoreListResult) =>
        !item.isDirectory ? (
          <EuiButtonIcon
            iconType="videoPlayer"
            aria-label="播放"
            onClick={() =>
              onPlay(
                relativeDir
                  ? `${relativeDir}/${item.fileName}`
                  : item.fileName,
              )
            }
          />
        ) : null,
      width: "60px",
    },
  ];

  return (
    <>
      {relativeDir ? (
        <EuiButtonEmpty iconType="returnKey" size="s" onClick={onGoUp}>
          返回上级
        </EuiButtonEmpty>
      ) : null}
      <EuiBasicTable items={files} columns={columns} />
    </>
  );
};
