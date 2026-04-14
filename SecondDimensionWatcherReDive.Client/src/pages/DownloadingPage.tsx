import { AlertTriangle } from "lucide-react";
import React from "react";
import { useSearchParams } from "react-router";

import { useDownloadingAnimations } from "../animation/hooks";
import { AnimationInfo } from "../components/AnimationInfo";
import { EmptyPrompt } from "../components/ui/EmptyPrompt";
import { Pagination } from "../components/ui/Pagination";
import { PAGE_SIZE } from "../config";
import { PageTemplate } from "./PageTemplate";

export const DownloadingPage: React.FC = () => {
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
      {error ? (
        <EmptyPrompt
          icon={<AlertTriangle size={48} />}
          title={<h2>加载失败</h2>}
          body={<p>无法获取数据，请稍后重试</p>}
        />
      ) : info && info.data.length > 0 ? (
        info.data.map((i) => <AnimationInfo value={i} key={i.id} />)
      ) : info ? (
        <EmptyPrompt title={<h2>暂无下载中的项目</h2>} />
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
