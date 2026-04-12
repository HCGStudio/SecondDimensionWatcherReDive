import { Loader2 } from "lucide-react";
import React from "react";

import { cn } from "../../lib/cn";

export interface SpinnerProps {
  size?: number;
  className?: string;
}

export const Spinner: React.FC<SpinnerProps> = ({
  size = 24,
  className,
}) => {
  return (
    <Loader2
      size={size}
      className={cn("animate-spin text-brand", className)}
    />
  );
};
