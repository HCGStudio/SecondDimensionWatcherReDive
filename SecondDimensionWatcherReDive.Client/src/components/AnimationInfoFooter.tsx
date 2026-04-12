import { FolderOpen } from "lucide-react";
import React from "react";

import { IAnimationInfo } from "../animation/IAnimationInfo";
import { useAnimationDownloadStatus } from "../animation/hooks";
import {
  cancelDownload,
  pauseDownload,
  resumeDownload,
  submitDownload,
} from "../animation/utils";
import {
  FinishedAnimationDownloadStatus,
  TrackingAnimationDownloadStatus,
} from "./AnimationDownloadStatus";
import { FileBrowser } from "./FileBrowser";
import { useToast } from "./ToastProvider";
import { Button } from "./ui/Button";
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetBody,
  SheetTitle,
} from "./ui/Sheet";

export interface IAnimationInfoFooterProps {
  value: IAnimationInfo;
}

interface IButtonSetProps {
  id: string;
}

const UntrackedButtonSet: React.FC<IButtonSetProps> = ({ id }) => {
  const { addToast } = useToast();

  const startDownload = React.useCallback(() => {
    submitDownload(id).catch(() => {
      addToast({ title: "下载失败", color: "danger", text: "无法提交下载任务" });
    });
  }, [id, addToast]);

  return (
    <Button size="sm" onClick={startDownload}>
      下载
    </Button>
  );
};

const TrackingButtonSet: React.FC<IButtonSetProps> = ({ id }) => {
  const { data: status } = useAnimationDownloadStatus(id);
  const { addToast } = useToast();

  const pause = React.useCallback(() => {
    pauseDownload(id).catch(() => {
      addToast({ title: "暂停失败", color: "danger", text: "无法暂停下载任务" });
    });
  }, [id, addToast]);

  const resume = React.useCallback(() => {
    resumeDownload(id).catch(() => {
      addToast({ title: "恢复失败", color: "danger", text: "无法恢复下载任务" });
    });
  }, [id, addToast]);

  const cancel = React.useCallback(() => {
    if (window.confirm("确定要取消下载并删除文件吗？")) {
      cancelDownload(id, true).catch(() => {
        addToast({ title: "删除失败", color: "danger", text: "无法取消下载任务" });
      });
    }
  }, [id, addToast]);

  return (
    <>
      {status ? (
        <>
          {status.state === "Downloading" ? (
            <Button size="sm" color="warning" onClick={pause}>
              暂停
            </Button>
          ) : null}
          {status.state === "Paused" ? (
            <Button size="sm" color="success" onClick={resume}>
              恢复
            </Button>
          ) : null}
          <Button size="sm" color="danger" onClick={cancel}>
            删除
          </Button>
        </>
      ) : null}
    </>
  );
};

export const AnimationInfoFooter: React.FC<IAnimationInfoFooterProps> = ({
  value,
}) => {
  const [isSheetOpen, setIsSheetOpen] = React.useState(false);

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-col gap-2">
        {value.isDownloadTracked && !value.isDownloadFinished ? (
          <TrackingAnimationDownloadStatus id={value.id} />
        ) : null}
        {value.isDownloadTracked && value.isDownloadFinished ? (
          <FinishedAnimationDownloadStatus />
        ) : null}
      </div>
      <div className="flex justify-end gap-2">
        {!value.isDownloadTracked ? (
          <UntrackedButtonSet id={value.id} />
        ) : null}
        {value.isDownloadTracked && !value.isDownloadFinished ? (
          <TrackingButtonSet id={value.id} />
        ) : null}
        {value.isDownloadTracked && value.isDownloadFinished ? (
          <Button
            size="sm"
            variant="outline"
            onClick={() => setIsSheetOpen(true)}
          >
            <FolderOpen size={16} />
            浏览文件
          </Button>
        ) : null}
      </div>

      <Sheet open={isSheetOpen} onOpenChange={setIsSheetOpen}>
        <SheetContent>
          <SheetHeader>
            <SheetTitle>{value.title}</SheetTitle>
          </SheetHeader>
          <SheetBody>
            <FileBrowser animationId={value.id} />
          </SheetBody>
        </SheetContent>
      </Sheet>
    </div>
  );
};
