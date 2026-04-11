import { EuiEmptyPrompt, EuiButton } from "@elastic/eui";
import React from "react";
import { useNavigate, useRouteError } from "react-router-dom";

import { PageTemplate } from "./PageTemplate";

export const ErrorPage: React.FC = () => {
  const error = useRouteError() as { statusText?: string; message?: string };
  const navigate = useNavigate();

  return (
    <PageTemplate>
      <EuiEmptyPrompt
        iconType="warning"
        title={<h2>页面出错了</h2>}
        body={<p>{error?.statusText || error?.message || "未知错误"}</p>}
        actions={
          <EuiButton fill onClick={() => navigate("/")}>
            返回主页
          </EuiButton>
        }
      />
    </PageTemplate>
  );
};
