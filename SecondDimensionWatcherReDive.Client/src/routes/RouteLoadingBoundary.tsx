import React from "react";
import { useTranslation } from "react-i18next";

import { AlertTriangle, RotateCcw } from "lucide-react";

import { Button } from "../components/ui/Button";
import { Spinner } from "../components/ui/Spinner";

interface RouteLoadingBoundaryState {
  error: unknown | null;
}

const RouteLoadingFallback: React.FC = () => {
  const { t } = useTranslation("errors");

  return (
    <main
      className="flex min-h-screen items-center justify-center bg-canvas px-6"
      aria-busy="true"
      aria-live="polite"
    >
      <div className="flex items-center gap-3 text-sm text-muted">
        <Spinner size={24} />
        <span>{t("routeLoading")}</span>
      </div>
    </main>
  );
};

const RouteLoadingFailure: React.FC = () => {
  const { t } = useTranslation("errors");

  return (
    <main className="flex min-h-screen items-center justify-center bg-canvas px-6">
      <div
        className="max-w-md rounded-xl border border-border bg-surface p-8 text-center shadow-ring"
        role="alert"
      >
        <AlertTriangle className="mx-auto text-warning" size={44} />
        <h1 className="mt-4 font-serif text-2xl font-medium text-foreground">
          {t("routeLoadFailed")}
        </h1>
        <p className="mt-2 text-sm text-muted">{t("routeLoadFailedDetail")}</p>
        <div className="mt-6 flex justify-center gap-3">
          <Button onClick={() => window.location.reload()}>
            <RotateCcw size={16} />
            {t("retry")}
          </Button>
          <Button variant="outline" onClick={() => window.location.assign("/")}>
            {t("backToHome")}
          </Button>
        </div>
      </div>
    </main>
  );
};

export class RouteLoadingBoundary extends React.Component<
  React.PropsWithChildren,
  RouteLoadingBoundaryState
> {
  public state: RouteLoadingBoundaryState = { error: null };

  public static getDerivedStateFromError(error: unknown) {
    return { error };
  }

  public render() {
    if (this.state.error) return <RouteLoadingFailure />;
    return (
      <React.Suspense fallback={<RouteLoadingFallback />}>
        {this.props.children}
      </React.Suspense>
    );
  }
}
