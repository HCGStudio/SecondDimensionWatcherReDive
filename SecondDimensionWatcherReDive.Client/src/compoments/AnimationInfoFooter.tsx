import { EuiButton, EuiFlexGroup, EuiFlexItem } from "@elastic/eui";
import React from "react";

import { IAnimationInfo } from "../animation/IAnimationInfo";
import { useAnimationDownloadStatus } from "../animation/hooks";
import {
  pauseDownload,
  resumeDownload,
  submitDownload,
} from "../animation/utils";
import {
  FinishedAnimationDownloadStatus,
  TrackingAnimationDownloadStatus,
} from "./AnimationDownloadStatus";

export interface IAnimationInfoFooterProps {
  value: IAnimationInfo;
}

interface IButtonSetProps {
  id: string;
}

const UntrackedButtonSet: React.FC<IButtonSetProps> = ({ id }) => {
  const startDownload = React.useCallback(() => {
    submitDownload(id).catch(console.error);
  }, [id]);

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

  const pause = React.useCallback(() => {
    pauseDownload(id).catch(console.error);
  }, [id]);

  const resume = React.useCallback(() => {
    resumeDownload(id).catch(console.error);
  }, [id]);

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
            <EuiButton size="s" fill color="danger">
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
