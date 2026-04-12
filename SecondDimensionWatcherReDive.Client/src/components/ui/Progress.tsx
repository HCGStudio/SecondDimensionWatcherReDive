import * as ProgressPrimitive from "@radix-ui/react-progress";
import React from "react";

import { cn } from "../../lib/cn";

type ProgressColor = "brand" | "error" | "warning" | "success";

export interface ProgressProps {
  value: number;
  max?: number;
  color?: ProgressColor;
  className?: string;
}

const indicatorColors: Record<ProgressColor, string> = {
  brand: "bg-brand",
  error: "bg-error",
  warning: "bg-warning",
  success: "bg-success",
};

export const Progress: React.FC<ProgressProps> = ({
  value,
  max = 100,
  color = "brand",
  className,
}) => {
  const percentage = Math.min(100, Math.max(0, (value / max) * 100));

  return (
    <ProgressPrimitive.Root
      className={cn(
        "relative h-2 w-full overflow-hidden rounded-full bg-border-light",
        className,
      )}
      value={value}
      max={max}
    >
      <ProgressPrimitive.Indicator
        className={cn(
          "h-full rounded-full transition-transform duration-300 ease-out",
          indicatorColors[color],
        )}
        style={{ width: `${percentage}%` }}
      />
    </ProgressPrimitive.Root>
  );
};
