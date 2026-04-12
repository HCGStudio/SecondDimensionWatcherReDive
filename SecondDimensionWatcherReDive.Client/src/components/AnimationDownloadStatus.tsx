import { ArrowDownNarrowWide, Clock } from "lucide-react";
import dayjs from "dayjs";
import React from "react";

import { useAnimationDownloadStatus } from "../animation/hooks";
import { formatBytes } from "../utils/formatBytes";
import { Progress } from "./ui/Progress";

export interface IAnimationDownloadStatusProps {
  id: string;
}

export const FinishedAnimationDownloadStatus: React.FC = () => {
  return (
    <div>
      <Progress color="success" value={100} max={100} />
    </div>
  );
};

const colorByState: Record<string, "brand" | "error" | "warning" | "success"> = {
  Downloading: "brand",
  Error: "error",
  Paused: "warning",
};

export const TrackingAnimationDownloadStatus: React.FC<
  IAnimationDownloadStatusProps
> = ({ id }) => {
  const { data: status } = useAnimationDownloadStatus(id);

  const color = status?.state ? colorByState[status.state] ?? "success" : "success";

  return (
    <>
      {status ? (
        <>
          <div>
            <Progress
              color={color}
              value={status.progress * 100}
              max={100}
            />
          </div>
          <div className="flex items-center gap-3 text-sm text-muted">
            <span className="inline-flex items-center gap-1">
              <Clock size={14} />
              {dayjs.duration({ seconds: status.remaining }).humanize()}
            </span>
            <span className="inline-flex items-center gap-1">
              <ArrowDownNarrowWide size={14} />
              {formatBytes(status.speed)}
            </span>
          </div>
        </>
      ) : null}
    </>
  );
};
