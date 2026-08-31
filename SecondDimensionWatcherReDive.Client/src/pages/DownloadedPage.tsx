import React from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";

import { AlertTriangle } from "lucide-react";

import { useDownloadedAnimations } from "../animation/hooks";
import { AnimationInfo } from "../components/AnimationInfo";
import { WebDavAccessSheet } from "../components/WebDavAccessSheet";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Pagination } from "../components/ui/Pagination";
import { PAGE_SIZE } from "../config";
import { PageTemplate } from "./PageTemplate";

export const DownloadedPage: React.FC = () => {
  const { t } = useTranslation(["animation", "errors"]);
  const [searchParams, setSearchParams] = useSearchParams();
  const actualPage = Math.max(
    1,
    Number.parseInt(searchParams.get("page") ?? "1") ?? 1,
  );
  const { data: info, error } = useDownloadedAnimations(
    (actualPage - 1) * PAGE_SIZE,
    PAGE_SIZE,
  );
  const navigateToPage = React.useCallback(
    (newPage: number) => {
      setSearchParams((params) => {
        params?.set("page", (newPage + 1).toString());
        return params;
      });
    },
    [setSearchParams],
  );

  const pageCount = React.useMemo(() => {
    if (!info?.totalItems) return 0;
    return Math.ceil(info.totalItems / PAGE_SIZE);
  }, [info?.totalItems]);

  return (
    <PageTemplate>
      <div className="mb-6 flex justify-end">
        <WebDavAccessSheet />
      </div>
      {error ? (
        <EmptyPrompt
          icon={<AlertTriangle size={48} />}
          title={<h2>{t("errors:loadFailed")}</h2>}
          body={<p>{t("errors:fetchFailed")}</p>}
        />
      ) : info && info.data.length > 0 ? (
        info.data.map((i) => <AnimationInfo value={i} key={i.id} />)
      ) : info ? (
        <EmptyPrompt title={<h2>{t("animation:empty.downloaded")}</h2>} />
      ) : null}
      {info && pageCount > 1 ? (
        <div className="mt-8 flex justify-center">
          <Pagination
            pageCount={pageCount}
            activePage={actualPage - 1}
            onPageClick={navigateToPage}
          />
        </div>
      ) : null}
    </PageTemplate>
  );
};
