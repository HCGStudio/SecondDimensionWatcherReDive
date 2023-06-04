import { EuiCard, EuiIcon, EuiSpacer } from "@elastic/eui";
import React from "react";

import { IAnimationInfo } from "../animation/IAnimationInfo";
import { AnimationInfoFooter } from "./AnimationInfoFooter";

export interface IAnimationInfoProps {
  value: IAnimationInfo;
}

export const AnimationInfo: React.FC<IAnimationInfoProps> = ({ value }) => {
  return (
    <>
      <EuiCard
        display="plain"
        hasBorder
        title={value.title}
        textAlign="left"
        icon={<EuiIcon type="videoPlayer" />}
        description={value.description}
        footer={<AnimationInfoFooter value={value} />}
      >
        {new Date(value.publishTime).toLocaleString()}
      </EuiCard>
      <EuiSpacer size="s" />
    </>
  );
};
