import { AlertTriangle } from "lucide-react";
import React from "react";
import { useNavigate, useRouteError } from "react-router";

import { Button } from "../components/ui/Button";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { PageTemplate } from "./PageTemplate";

export const ErrorPage: React.FC = () => {
  const error = useRouteError() as { statusText?: string; message?: string };
  const navigate = useNavigate();

  return (
    <PageTemplate>
      <EmptyPrompt
        icon={<AlertTriangle size={48} />}
        title={<h2>页面出错了</h2>}
        body={<p>{error?.statusText || error?.message || "未知错误"}</p>}
        actions={
          <Button onClick={() => navigate("/")}>
            返回主页
          </Button>
        }
      />
    </PageTemplate>
  );
};
