import { Play } from "lucide-react";
import React from "react";

import { IAnimationInfo } from "../animation/IAnimationInfo";
import { AnimationInfoFooter } from "./AnimationInfoFooter";
import { Card } from "./ui/Card";

export interface IAnimationInfoProps {
  value: IAnimationInfo;
}

export const AnimationInfo: React.FC<IAnimationInfoProps> = ({ value }) => {
  return (
    <div className="mb-3">
      <Card
        icon={<Play size={20} />}
        title={value.title}
        description={value.description}
        footer={<AnimationInfoFooter value={value} />}
      >
        <p className="text-sm text-subtle">
          {new Date(value.publishTime).toLocaleString()}
        </p>
      </Card>
    </div>
  );
};
