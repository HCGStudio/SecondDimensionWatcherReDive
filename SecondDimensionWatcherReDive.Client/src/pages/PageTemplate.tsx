import React from "react";

import { AppHeader } from "../components/AppHeader";

export interface IPageTemplateProps extends React.PropsWithChildren {}

export const PageTemplate: React.FC<IPageTemplateProps> = ({ children }) => {
  return (
    <div className="min-h-screen bg-canvas">
      <AppHeader />
      <main className="mx-auto max-w-7xl px-4 py-6 sm:px-6 sm:py-8">
        {children}
      </main>
    </div>
  );
};
