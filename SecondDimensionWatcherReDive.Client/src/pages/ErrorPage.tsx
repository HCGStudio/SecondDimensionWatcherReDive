import { AlertTriangle } from "lucide-react";
import React from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useRouteError } from "react-router";

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
          <Button onClick={() => navigate("/")}>
            {t("backToHome")}
          </Button>
        }
      />
    </PageTemplate>
  );
};
