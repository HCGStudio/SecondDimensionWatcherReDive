import { EuiFlexGroup, EuiPagination, EuiEmptyPrompt } from "@elastic/eui";
import React from "react";
import { useSearchParams } from "react-router-dom";

import { useDownloadingAnimations } from "../animation/hooks";
import { AnimationInfo } from "../compoments/AnimationInfo";
import { PAGE_SIZE } from "../config";
import { PageTemplate } from "./PageTemplate";

export const DownloadingPage: React.FC = () => {
  const [searchParams, setSearchParams] = useSearchParams();
  const actualPage = Math.max(
    1,
    Number.parseInt(searchParams.get("page") ?? "1") ?? 1,
  );
  const { data: info } = useDownloadingAnimations(
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
      {info && info.data.length > 0 ? (
        info.data.map((i) => <AnimationInfo value={i} key={i.id} />)
      ) : info ? (
        <EuiEmptyPrompt title={<h2>暂无下载中的项目</h2>} />
      ) : null}
      {info && pageCount > 1 ? (
        <EuiFlexGroup justifyContent="spaceAround">
          <EuiPagination
            aria-label="Pagination"
            pageCount={pageCount}
            activePage={actualPage - 1}
            onPageClick={navigateToPage}
          />
        </EuiFlexGroup>
      ) : null}
    </PageTemplate>
  );
};
