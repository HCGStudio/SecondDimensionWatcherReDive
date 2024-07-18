import { EuiFlexItem, EuiIcon, EuiProgress, EuiText } from "@elastic/eui";
import dayjs from "dayjs";
import React from "react";

import { useAnimationDownloadStatus } from "../animation/hooks";
import { formatBytes } from "../utils/formatBytes";

export interface IAnimationDownloadStatusProps {
  id: string;
}

export const FinishedAnimationDownloadStatus: React.FC = () => {
  return (
    <EuiFlexItem grow={false}>
      <EuiProgress color="success" value={100} max={100} size="m"></EuiProgress>
    </EuiFlexItem>
  );
};

export const TrackingAnimationDownloadStatus: React.FC<
  IAnimationDownloadStatusProps
> = ({ id }) => {
  const { data: status } = useAnimationDownloadStatus(id);
  const colorByStatus = React.useMemo(() => {
    switch (status?.state) {
      case "Downloading":
        return "primary";
      case "Error":
        return "danger";
      case "Paused":
        return "warning";
      default:
        return "success";
    }
  }, [status?.state]);
  return (
    <>
      {status ? (
        <>
          <EuiFlexItem grow={false}>
            <EuiProgress
              color={colorByStatus}
              value={status.progress * 100}
              max={100}
              size="m"
            ></EuiProgress>
          </EuiFlexItem>
          <EuiFlexItem grow={false}>
            <EuiText size="s">
              <EuiIcon type="clock" />
              {dayjs.duration({ seconds: status.remaining }).humanize()}
              &nbsp;
              <EuiIcon type="sortDown" />
              {formatBytes(status.speed)}
            </EuiText>
          </EuiFlexItem>
        </>
      ) : null}
    </>
  );
};
