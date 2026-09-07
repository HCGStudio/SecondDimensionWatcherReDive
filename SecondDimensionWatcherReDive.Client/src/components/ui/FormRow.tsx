import React from "react";

import { cn } from "../../lib/cn";

export interface FormRowProps {
  label?: string;
  htmlFor?: string;
  isInvalid?: boolean;
  error?: string[];
  hasEmptyLabelSpace?: boolean;
  className?: string;
  children: React.ReactNode;
}

export const FormRow: React.FC<FormRowProps> = ({
  label,
  htmlFor,
  isInvalid,
  error,
  hasEmptyLabelSpace,
  className,
  children,
}) => {
  const generatedId = React.useId();
  const firstControl = React.Children.toArray(children).find((child) =>
    React.isValidElement(child),
  ) as React.ReactElement<Record<string, unknown>> | undefined;
  const childId =
    typeof firstControl?.props.id === "string"
      ? firstControl.props.id
      : undefined;
  const controlId = htmlFor ?? childId ?? `form-control-${generatedId}`;
  const errorId = `${controlId}-error`;
  let linkedControl = false;
  const enhancedChildren = htmlFor
    ? children
    : React.Children.map(children, (child) => {
        if (linkedControl || !React.isValidElement(child)) return child;
        linkedControl = true;
        const element = child as React.ReactElement<Record<string, unknown>>;
        const existingDescription = element.props["aria-describedby"];
        return React.cloneElement(element, {
          id:
            typeof element.props.id === "string" ? element.props.id : controlId,
          "aria-invalid":
            element.props["aria-invalid"] ?? (isInvalid || undefined),
          "aria-describedby": isInvalid
            ? [existingDescription, errorId].filter(Boolean).join(" ")
            : existingDescription,
        });
      });

  return (
    <div className={cn("flex flex-col", className)}>
      {label ? (
        <label
          htmlFor={controlId}
          className="mb-1.5 text-sm font-medium text-foreground"
        >
          {label}
        </label>
      ) : hasEmptyLabelSpace ? (
        <div aria-hidden="true" className="mb-1.5 text-sm">
          &nbsp;
        </div>
      ) : null}
      {enhancedChildren}
      {isInvalid && error?.length ? (
        <p
          id={errorId}
          role="status"
          aria-live="polite"
          className="mt-1 text-sm text-error"
        >
          {error[0]}
        </p>
      ) : null}
    </div>
  );
};
