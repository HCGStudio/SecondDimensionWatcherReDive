import { EuiButton, EuiFlexGroup, EuiFlexItem } from "@elastic/eui";
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
import { useToast } from "./ToastProvider";

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
    <EuiFlexItem grow={false}>
      <EuiButton size="s" onClick={startDownload}>
        下载
      </EuiButton>
    </EuiFlexItem>
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
            <EuiFlexItem grow={false}>
              <EuiButton size="s" fill color="warning" onClick={pause}>
                暂停
              </EuiButton>
            </EuiFlexItem>
          ) : null}
          {status.state === "Paused" ? (
            <EuiFlexItem grow={false}>
              <EuiButton
                size="s"
                fill
                style={{ color: "#FFF" }}
                color="success"
                onClick={resume}
              >
                恢复
              </EuiButton>
            </EuiFlexItem>
          ) : null}

          <EuiFlexItem grow={false}>
            <EuiButton size="s" fill color="danger" onClick={cancel}>
              删除
            </EuiButton>
          </EuiFlexItem>
        </>
      ) : null}
    </>
  );
};

export const AnimationInfoFooter: React.FC<IAnimationInfoFooterProps> = ({
  value,
}) => {
  return (
    <EuiFlexGroup direction="column">
      <EuiFlexItem>
        <EuiFlexGroup direction="column">
          {value.isDownloadTracked && !value.isDownloadFinished ? (
            <TrackingAnimationDownloadStatus id={value.id} />
          ) : null}
          {value.isDownloadTracked && value.isDownloadFinished ? (
            <FinishedAnimationDownloadStatus />
          ) : null}
        </EuiFlexGroup>
      </EuiFlexItem>
      <EuiFlexItem>
        <EuiFlexGroup justifyContent="flexEnd">
          {!value.isDownloadTracked ? (
            <UntrackedButtonSet id={value.id} />
          ) : null}
          {value.isDownloadTracked && !value.isDownloadFinished ? (
            <TrackingButtonSet id={value.id} />
          ) : null}
        </EuiFlexGroup>
      </EuiFlexItem>
    </EuiFlexGroup>
  );
};
