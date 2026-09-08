import React from "react";

import { AppHeader } from "../components/AppHeader";
import { cn } from "../lib/cn";

export interface IPageTemplateProps extends React.PropsWithChildren {
  className?: string;
}

export const PageTemplate: React.FC<IPageTemplateProps> = ({
  children,
  className,
}) => {
  return (
    <div className="min-h-screen bg-canvas">
      <AppHeader />
      <div className="min-w-0 lg:pl-[216px]">
        <main
          className={cn(
            "mx-auto max-w-[1600px] px-5 py-7 sm:px-7 sm:py-8 lg:px-10 lg:py-9",
            className,
          )}
        >
          {children}
        </main>
      </div>
    </div>
  );
};
