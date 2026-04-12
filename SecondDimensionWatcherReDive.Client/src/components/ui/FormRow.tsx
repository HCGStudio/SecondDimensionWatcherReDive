import React from "react";

import { cn } from "../../lib/cn";

export interface FormRowProps {
  label?: string;
  isInvalid?: boolean;
  error?: string[];
  hasEmptyLabelSpace?: boolean;
  className?: string;
  children: React.ReactNode;
}

export const FormRow: React.FC<FormRowProps> = ({
  label,
  isInvalid,
  error,
  hasEmptyLabelSpace,
  className,
  children,
}) => {
  return (
    <div className={cn("flex flex-col", className)}>
      {label ? (
        <label className="mb-1.5 text-sm font-medium text-foreground">
          {label}
        </label>
      ) : hasEmptyLabelSpace ? (
        <div className="mb-1.5 text-sm">&nbsp;</div>
      ) : null}
      {children}
      {isInvalid && error?.length ? (
        <p className="mt-1 text-sm text-error">{error[0]}</p>
      ) : null}
    </div>
  );
};
