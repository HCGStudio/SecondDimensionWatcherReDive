import React from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";

import { AlertTriangle, Download } from "lucide-react";

import { useDownloadingAnimations } from "../animation/hooks";
import { AnimationInfo } from "../components/AnimationInfo";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Pagination } from "../components/ui/Pagination";
import { Spinner } from "../components/ui/Spinner";
import { PAGE_SIZE } from "../config";
import { PageTemplate } from "./PageTemplate";

export const DownloadingPage: React.FC = () => {
  const { t } = useTranslation(["animation", "errors", "common"]);
  const [searchParams, setSearchParams] = useSearchParams();
  const actualPage = Math.max(
    1,
    Number.parseInt(searchParams.get("page") ?? "1") ?? 1,
  );
  const { data: info, error } = useDownloadingAnimations(
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
      <header className="mb-6 flex items-center gap-3">
        <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-tint text-brand">
          <Download size={21} aria-hidden="true" />
        </span>
        <div>
          <h1 className="font-sans text-2xl font-semibold tracking-tight text-foreground">
            {t("common:nav.downloading")}
          </h1>
          {info ? (
            <p className="mt-1 text-sm text-muted">
              {t("animation:episodeList.resultCount", {
                visible: info.data.length,
                total: info.totalItems,
              })}
            </p>
          ) : null}
        </div>
      </header>

      <section className="overflow-hidden rounded-xl border border-border bg-surface">
        {error ? (
          <EmptyPrompt
            icon={<AlertTriangle size={48} />}
            title={<h2>{t("errors:loadFailed")}</h2>}
            body={<p>{t("errors:fetchFailed")}</p>}
          />
        ) : info && info.data.length > 0 ? (
          info.data.map((i) => <AnimationInfo value={i} key={i.id} />)
        ) : info ? (
          <EmptyPrompt
            icon={<Download size={36} />}
            title={<h2>{t("animation:empty.downloading")}</h2>}
          />
        ) : (
          <div className="flex justify-center py-16">
            <Spinner />
          </div>
        )}
      </section>
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
