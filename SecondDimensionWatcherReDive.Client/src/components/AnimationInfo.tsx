import { Play } from "lucide-react";
import React from "react";

import { IAnimationInfo } from "../animation/IAnimationInfo";
import { AnimationInfoFooter } from "./AnimationInfoFooter";
import { Card } from "./ui/Card";

export interface IAnimationInfoProps {
  value: IAnimationInfo;
}

function formatEpisodeTag(
  season?: number | null,
  episode?: number | null,
): string | null {
  if (season == null && episode == null) return null;
  const s = season != null ? `S${String(season).padStart(2, "0")}` : "";
  const e = episode != null ? `E${String(episode).padStart(2, "0")}` : "";
  return s + e;
}

export const AnimationInfo: React.FC<IAnimationInfoProps> = ({ value }) => {
  const tag = formatEpisodeTag(value.season, value.episode);

  return (
    <div className="mb-3">
      <Card
        icon={<Play size={20} />}
        title={value.title}
        description={value.description}
        footer={<AnimationInfoFooter value={value} />}
      >
        <div className="flex items-center gap-3 text-sm text-subtle">
          <span>{new Date(value.publishTime).toLocaleString()}</span>
          {tag ? (
            <span className="rounded bg-accent/10 px-1.5 py-0.5 font-mono text-xs text-accent">
              {tag}
            </span>
          ) : null}
        </div>
      </Card>
    </div>
  );
};
