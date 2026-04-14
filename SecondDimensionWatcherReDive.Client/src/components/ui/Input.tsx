import React from "react";

import { cn } from "../../lib/cn";

export interface InputProps
  extends React.InputHTMLAttributes<HTMLInputElement> {
  isInvalid?: boolean;
  ref?: React.Ref<HTMLInputElement>;
}

export const Input: React.FC<InputProps> = ({
  className,
  isInvalid,
  ref,
  ...props
}) => {
  return (
    <input
      ref={ref}
      className={cn(
        "w-full rounded-lg border bg-surface px-3 py-2 text-sm text-foreground placeholder:text-subtle transition-colors",
        "focus:outline-hidden focus:ring-2 focus:ring-focus focus:border-focus",
        isInvalid
          ? "border-error focus:ring-error"
          : "border-border",
        className,
      )}
      {...props}
    />
  );
};
