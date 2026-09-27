import React from "react";
import { useTranslation } from "react-i18next";
import { Navigate } from "react-router";

import { useLoginStatus } from "../auth/hooks";
import { getAuthResult } from "../auth/httpClient";
import { apiErrorStatus } from "../errors/apiError";
import { Button } from "./ui/Button";
import { EmptyPrompt } from "./ui/EmptyPrompt";

export const ProtectedRoute: React.FC<React.PropsWithChildren> = ({
  children,
}) => {
  const { t } = useTranslation("errors");
  const { data: status, error, mutate, isValidating } = useLoginStatus();

  if (!getAuthResult() || apiErrorStatus(error) === 401) {
    return <Navigate to="/login" replace />;
  }

  // SWR retains successful data when revalidation fails. A temporary outage
  // must not turn that still-authenticated page into a login redirect.
  if (status) return <>{children}</>;

  if (error) {
    return (
      <main className="flex min-h-screen items-center justify-center bg-canvas px-6">
        <EmptyPrompt
          role="alert"
          title={<h1>{t("loadFailed")}</h1>}
          body={<p>{t("fetchFailed")}</p>}
          actions={
            <Button
              disabled={isValidating}
              onClick={() => void mutate().catch(() => undefined)}
            >
              {t("retry")}
            </Button>
          }
        />
      </main>
    );
  }

  return null;
};
