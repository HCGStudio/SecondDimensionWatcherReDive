import React from "react";

import { cn } from "../../lib/cn";

export interface EmptyPromptProps {
  icon?: React.ReactNode;
  title: React.ReactNode;
  body?: React.ReactNode;
  actions?: React.ReactNode;
  className?: string;
}

export const EmptyPrompt: React.FC<EmptyPromptProps> = ({
  icon,
  title,
  body,
  actions,
  className,
}) => {
  return (
    <div
      className={cn(
        "flex flex-col items-center justify-center py-16 text-center",
        className,
      )}
    >
      {icon ? <div className="mb-4 text-subtle">{icon}</div> : null}
      <div className="font-serif text-xl font-medium leading-heading text-foreground">
        {title}
      </div>
      {body ? (
        <div className="mt-2 max-w-md text-sm leading-body text-muted">
          {body}
        </div>
      ) : null}
      {actions ? <div className="mt-6">{actions}</div> : null}
    </div>
  );
};
