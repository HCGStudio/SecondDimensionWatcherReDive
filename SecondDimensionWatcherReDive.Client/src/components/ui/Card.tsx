import React from "react";

import { cn } from "../../lib/cn";

export interface CardProps {
  icon?: React.ReactNode;
  title: React.ReactNode;
  description?: React.ReactNode;
  footer?: React.ReactNode;
  className?: string;
  children?: React.ReactNode;
}

export const Card: React.FC<CardProps> = ({
  icon,
  title,
  description,
  footer,
  className,
  children,
}) => {
  return (
    <div
      className={cn(
        "rounded-md border border-border bg-surface p-5 shadow-whisper",
        className,
      )}
    >
      <div className="flex items-start gap-3">
        {icon ? <div className="mt-0.5 shrink-0 text-muted">{icon}</div> : null}
        <div className="min-w-0 flex-1">
          <h3 className="font-serif text-lg font-medium leading-heading text-foreground">
            {title}
          </h3>
          {description ? (
            <p className="mt-1 text-sm leading-body text-muted">
              {description}
            </p>
          ) : null}
        </div>
      </div>
      {children ? <div className="mt-3">{children}</div> : null}
      {footer ? (
        <div className="mt-4 border-t border-border-light pt-4">{footer}</div>
      ) : null}
    </div>
  );
};
