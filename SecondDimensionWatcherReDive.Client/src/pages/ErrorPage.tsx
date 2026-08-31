import React from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useRouteError } from "react-router";

import { AlertTriangle, RotateCcw } from "lucide-react";

import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { PageTemplate } from "./PageTemplate";

export const ErrorPage: React.FC = () => {
  const { t } = useTranslation("errors");
  const error = useRouteError() as { statusText?: string; message?: string };
  const navigate = useNavigate();

  return (
    <PageTemplate>
      <EmptyPrompt
        icon={<AlertTriangle size={48} />}
        title={<h2>{t("pageError")}</h2>}
        body={<p>{error?.statusText || error?.message || t("unknown")}</p>}
        actions={
          <div className="flex flex-wrap justify-center gap-3">
            <Button onClick={() => window.location.reload()}>
              <RotateCcw size={16} />
              {t("retry")}
            </Button>
            <Button variant="outline" onClick={() => navigate("/")}>
              {t("backToHome")}
            </Button>
          </div>
        }
      />
    </PageTemplate>
  );
};
