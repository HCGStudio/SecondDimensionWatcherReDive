import React from "react";

import { cn } from "../../lib/cn";

export interface InputProps
  extends React.InputHTMLAttributes<HTMLInputElement> {
  isInvalid?: boolean;
}

export const Input = React.forwardRef<HTMLInputElement, InputProps>(
  ({ className, isInvalid, ...props }, ref) => {
    return (
      <input
        ref={ref}
        className={cn(
          "w-full rounded-lg border bg-surface px-3 py-2 text-sm text-foreground placeholder:text-subtle transition-colors",
          "focus:outline-none focus:ring-2 focus:ring-focus focus:border-focus",
          isInvalid
            ? "border-error focus:ring-error"
            : "border-border",
          className,
        )}
        {...props}
      />
    );
  },
);

Input.displayName = "Input";
