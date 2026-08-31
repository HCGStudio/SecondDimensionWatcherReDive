import React from "react";

import { cn } from "../../lib/cn";

export interface TableColumn<T> {
  field?: string;
  name: string;
  render?: (value: any, item: T) => React.ReactNode;
  width?: string;
  truncateText?: boolean;
}

export interface TableProps<T> {
  items: T[];
  columns: TableColumn<T>[];
  className?: string;
}

export function Table<T extends Record<string, any>>({
  items,
  columns,
  className,
}: TableProps<T>) {
  return (
    <div className={cn("w-full overflow-x-auto", className)}>
      <table className="w-full border-collapse">
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
            <tr key={rowIdx} className="transition-colors hover:bg-canvas">
              {columns.map((col, colIdx) => {
                const value = col.field ? (item as any)[col.field] : item;
                return (
                  <td
                    key={colIdx}
                    className={cn(
                      "border-b border-border-light px-4 py-3 text-sm",
                      col.truncateText && "max-w-0 truncate",
                    )}
                    style={col.width ? { width: col.width } : undefined}
                  >
                    {col.render ? col.render(value, item) : String(value ?? "")}
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
