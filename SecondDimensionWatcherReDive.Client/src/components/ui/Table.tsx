import React from "react";

import { cn } from "../../lib/cn";

export interface TableColumn<T> {
  field?: string;
  name: string;
  render?: (value: any, item: T) => React.ReactNode;
  width?: string;
  truncateText?: boolean;
  mobile?: "primary" | "secondary" | "hidden";
}

export interface TableProps<T> {
  items: T[];
  columns: TableColumn<T>[];
  className?: string;
  label?: string;
  rowKey?: (item: T, index: number) => React.Key;
}

export function Table<T extends Record<string, any>>({
  items,
  columns,
  className,
  label,
  rowKey,
}: TableProps<T>) {
  const renderValue = (item: T, column: TableColumn<T>) => {
    const value = column.field ? item[column.field] : item;
    return column.render ? column.render(value, item) : String(value ?? "");
  };

  const mobileColumns = columns.filter((column) => column.mobile !== "hidden");

  return (
    <div className={cn("w-full", className)}>
      <ul className="space-y-3 md:hidden" aria-label={label}>
        {items.map((item, rowIndex) => (
          <li
            key={rowKey?.(item, rowIndex) ?? rowIndex}
            className="rounded-lg border border-border bg-surface p-4 shadow-ring"
          >
            <dl className="space-y-3">
              {mobileColumns.map((column, columnIndex) => (
                <div
                  key={`${column.field ?? column.name}-${columnIndex}`}
                  className={cn(
                    column.mobile === "primary"
                      ? "border-b border-border-light pb-3"
                      : "grid grid-cols-[minmax(0,6.5rem)_minmax(0,1fr)] items-start gap-3",
                  )}
                >
                  <dt
                    className={cn(
                      "text-xs font-medium text-subtle",
                      column.mobile === "primary" && "sr-only",
                    )}
                  >
                    {column.name}
                  </dt>
                  <dd
                    className={cn(
                      "min-w-0 break-words text-sm text-foreground",
                      column.mobile === "primary" &&
                        "text-base font-medium leading-heading",
                    )}
                  >
                    {renderValue(item, column)}
                  </dd>
                </div>
              ))}
            </dl>
          </li>
        ))}
      </ul>
      <div className="hidden w-full overflow-x-auto md:block">
        <table className="w-full border-collapse">
          {label ? <caption className="sr-only">{label}</caption> : null}
          <thead>
            <tr>
              {columns.map((col, i) => (
                <th
                  key={i}
                  className="border-b border-border px-4 py-3 text-left text-sm font-medium text-muted"
                  style={col.width ? { width: col.width } : undefined}
                >
                  {col.name}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {items.map((item, rowIdx) => (
              <tr
                key={rowKey?.(item, rowIdx) ?? rowIdx}
                className="transition-colors hover:bg-canvas"
              >
                {columns.map((col, colIdx) => {
                  return (
                    <td
                      key={colIdx}
                      className={cn(
                        "border-b border-border-light px-4 py-3 text-sm",
                        col.truncateText && "max-w-0 truncate",
                      )}
                      style={col.width ? { width: col.width } : undefined}
                    >
                      {renderValue(item, col)}
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
