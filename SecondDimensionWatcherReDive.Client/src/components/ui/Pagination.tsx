import {
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
} from "lucide-react";
import React from "react";

import { cn } from "../../lib/cn";

export interface PaginationProps {
  pageCount: number;
  activePage: number;
  onPageClick: (page: number) => void;
}

export const Pagination: React.FC<PaginationProps> = ({
  pageCount,
  activePage,
  onPageClick,
}) => {
  if (pageCount <= 1) return null;

  const pages = React.useMemo(() => {
    const result: (number | "ellipsis")[] = [];
    const maxVisible = 7;

    if (pageCount <= maxVisible) {
      for (let i = 0; i < pageCount; i++) result.push(i);
    } else {
      result.push(0);
      if (activePage > 2) result.push("ellipsis");

      const start = Math.max(1, activePage - 1);
      const end = Math.min(pageCount - 2, activePage + 1);

      for (let i = start; i <= end; i++) result.push(i);

      if (activePage < pageCount - 3) result.push("ellipsis");
      result.push(pageCount - 1);
    }

    return result;
  }, [pageCount, activePage]);

  const btnBase =
    "inline-flex items-center justify-center rounded-md transition-colors focus:outline-none focus:ring-2 focus:ring-focus";

  return (
    <nav className="flex items-center gap-1" aria-label="Pagination">
      <button
        className={cn(btnBase, "p-1.5 text-muted hover:text-foreground")}
        onClick={() => onPageClick(0)}
        disabled={activePage === 0}
        aria-label="第一页"
      >
        <ChevronsLeft size={16} />
      </button>
      <button
        className={cn(btnBase, "p-1.5 text-muted hover:text-foreground")}
        onClick={() => onPageClick(activePage - 1)}
        disabled={activePage === 0}
        aria-label="上一页"
      >
        <ChevronLeft size={16} />
      </button>

      {pages.map((page, i) =>
        page === "ellipsis" ? (
          <span key={`e${i}`} className="px-1 text-subtle">
            ...
          </span>
        ) : (
          <button
            key={page}
            className={cn(
              btnBase,
              "min-w-[32px] px-2 py-1 text-sm",
              page === activePage
                ? "bg-brand text-white shadow-ring-brand"
                : "text-foreground hover:bg-canvas",
            )}
            onClick={() => onPageClick(page)}
            aria-label={`第 ${page + 1} 页`}
            aria-current={page === activePage ? "page" : undefined}
          >
            {page + 1}
          </button>
        ),
      )}

      <button
        className={cn(btnBase, "p-1.5 text-muted hover:text-foreground")}
        onClick={() => onPageClick(activePage + 1)}
        disabled={activePage === pageCount - 1}
        aria-label="下一页"
      >
        <ChevronRight size={16} />
      </button>
      <button
        className={cn(btnBase, "p-1.5 text-muted hover:text-foreground")}
        onClick={() => onPageClick(pageCount - 1)}
        disabled={activePage === pageCount - 1}
        aria-label="最后一页"
      >
        <ChevronsRight size={16} />
      </button>
    </nav>
  );
};
