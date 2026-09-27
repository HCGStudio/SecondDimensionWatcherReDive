import React from "react";
import { useTranslation } from "react-i18next";

import * as DialogPrimitive from "@radix-ui/react-dialog";

import { CalendarDays, ChevronLeft, ChevronRight, X } from "lucide-react";

import { cn } from "../../lib/cn";
import { Button } from "./Button";
import { Select, SelectItem } from "./Select";

const formatDate = (date: Date) =>
  `${String(date.getFullYear()).padStart(4, "0")}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;

function parseDate(value: string) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
  const date = new Date(`${value}T12:00:00`);
  return Number.isFinite(date.getTime()) &&
    date.getFullYear() >= 1 &&
    formatDate(date) === value
    ? date
    : null;
}

function shiftMonth(date: Date, amount: number) {
  const next = new Date(date);
  next.setDate(1);
  next.setMonth(next.getMonth() + amount);
  const last = new Date(next);
  last.setMonth(last.getMonth() + 1);
  last.setDate(0);
  next.setDate(Math.min(date.getDate(), last.getDate()));
  return next;
}

interface DatePickerProps extends Omit<
  React.ComponentPropsWithoutRef<typeof Button>,
  "value" | "onChange" | "children"
> {
  value: string;
  onValueChange: (value: string) => void;
}

export function DatePicker({
  value,
  onValueChange,
  className,
  ...props
}: DatePickerProps) {
  const { t, i18n } = useTranslation("common");
  const [open, setOpen] = React.useState(false);
  const [cursor, setCursor] = React.useState(
    () => parseDate(value) ?? new Date(),
  );
  const focusedDay = React.useRef<HTMLButtonElement>(null);
  const moveFocus = React.useRef(false);
  const locale = i18n.resolvedLanguage ?? i18n.language;
  const firstWeekday = locale.startsWith("zh") ? 1 : 0;
  const monthFormatter = new Intl.DateTimeFormat(locale, { month: "long" });
  const weekdayFormatter = new Intl.DateTimeFormat(locale, {
    weekday: "short",
  });
  const dateFormatter = new Intl.DateTimeFormat(locale, { dateStyle: "full" });
  const selected = parseDate(value);
  const year = cursor.getFullYear();
  const month = cursor.getMonth();
  const first = new Date(cursor);
  first.setDate(1);
  const offset = (first.getDay() - firstWeekday + 7) % 7;
  const last = new Date(first);
  last.setMonth(month + 1);
  last.setDate(0);
  const weeks = Math.ceil((offset + last.getDate()) / 7);
  const today = formatDate(new Date());
  const years = Array.from(
    { length: 21 },
    (_, index) => year - 10 + index,
  ).filter((candidate) => candidate >= 1 && candidate <= 9999);

  React.useEffect(() => {
    if (!moveFocus.current) return;
    moveFocus.current = false;
    focusedDay.current?.focus();
  }, [cursor]);

  const navigate = (next: Date, focus = false) => {
    if (next.getFullYear() < 1 || next.getFullYear() > 9999) return;
    moveFocus.current = focus;
    setCursor(next);
  };
  const choose = (nextValue: string) => {
    onValueChange(nextValue);
    setOpen(false);
  };
  const onDayKeyDown = (event: React.KeyboardEvent<HTMLButtonElement>) => {
    const dayOfWeek = (cursor.getDay() - firstWeekday + 7) % 7;
    const offsets: Record<string, number> = {
      ArrowLeft: -1,
      ArrowRight: 1,
      ArrowUp: -7,
      ArrowDown: 7,
      Home: -dayOfWeek,
      End: 6 - dayOfWeek,
    };
    if (event.key in offsets) {
      event.preventDefault();
      const next = new Date(cursor);
      next.setDate(next.getDate() + offsets[event.key]);
      navigate(next, true);
    } else if (event.key === "PageUp" || event.key === "PageDown") {
      event.preventDefault();
      navigate(
        shiftMonth(
          cursor,
          (event.key === "PageUp" ? -1 : 1) * (event.shiftKey ? 12 : 1),
        ),
        true,
      );
    }
  };

  return (
    <DialogPrimitive.Root
      open={open}
      onOpenChange={(nextOpen) => {
        if (nextOpen) setCursor(parseDate(value) ?? new Date());
        setOpen(nextOpen);
      }}
    >
      <DialogPrimitive.Trigger asChild>
        <Button
          variant="outline"
          className={cn("w-full justify-between font-normal", className)}
          {...props}
        >
          <span>
            {selected
              ? new Intl.DateTimeFormat(locale).format(selected)
              : t("datePicker.placeholder")}
          </span>
          <CalendarDays
            size={16}
            className="shrink-0 text-subtle"
            aria-hidden="true"
          />
        </Button>
      </DialogPrimitive.Trigger>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 z-[70] bg-black/40" />
        <DialogPrimitive.Content
          className="fixed left-1/2 top-1/2 z-[80] max-h-[calc(100dvh-2rem)] w-[calc(100vw_-_2rem)] max-w-sm -translate-x-1/2 -translate-y-1/2 overflow-y-auto rounded-2xl border border-border bg-surface p-5 text-foreground shadow-whisper"
          onOpenAutoFocus={(event) => {
            event.preventDefault();
            focusedDay.current?.focus();
          }}
        >
          <DialogPrimitive.Title className="pr-6 text-lg font-medium">
            {t("datePicker.title")}
          </DialogPrimitive.Title>
          <DialogPrimitive.Description className="sr-only">
            {t("datePicker.keyboardHelp")}
          </DialogPrimitive.Description>
          <div className="my-4 flex items-center gap-2">
            <Button
              variant="icon"
              size="sm"
              aria-label={t("datePicker.previousMonth")}
              onClick={() => navigate(shiftMonth(cursor, -1), true)}
              disabled={year === 1 && month === 0}
            >
              <ChevronLeft size={16} />
            </Button>
            <Select
              value={String(month)}
              onValueChange={(next) =>
                navigate(shiftMonth(cursor, Number(next) - month))
              }
              aria-label={t("datePicker.month")}
            >
              {Array.from({ length: 12 }, (_, index) => (
                <SelectItem key={index} value={String(index)}>
                  {monthFormatter.format(new Date(2024, index, 1))}
                </SelectItem>
              ))}
            </Select>
            <Select
              value={String(year)}
              onValueChange={(next) =>
                navigate(shiftMonth(cursor, (Number(next) - year) * 12))
              }
              aria-label={t("datePicker.year")}
            >
              {years.map((candidate) => (
                <SelectItem key={candidate} value={String(candidate)}>
                  {candidate}
                </SelectItem>
              ))}
            </Select>
            <Button
              variant="icon"
              size="sm"
              aria-label={t("datePicker.nextMonth")}
              onClick={() => navigate(shiftMonth(cursor, 1), true)}
              disabled={year === 9999 && month === 11}
            >
              <ChevronRight size={16} />
            </Button>
          </div>
          <div
            role="grid"
            aria-label={new Intl.DateTimeFormat(locale, {
              year: "numeric",
              month: "long",
            }).format(cursor)}
          >
            <div role="row" className="grid grid-cols-7">
              {Array.from({ length: 7 }, (_, index) => (
                <div
                  role="columnheader"
                  key={index}
                  className="py-2 text-center text-xs text-muted"
                >
                  {weekdayFormatter.format(
                    new Date(2024, 0, 7 + firstWeekday + index),
                  )}
                </div>
              ))}
            </div>
            {Array.from({ length: weeks }, (_, week) => (
              <div role="row" key={week} className="grid grid-cols-7">
                {Array.from({ length: 7 }, (_, column) => {
                  const day = week * 7 + column - offset + 1;
                  if (day < 1 || day > last.getDate())
                    return <div role="gridcell" key={column} />;
                  const date = new Date(first);
                  date.setDate(day);
                  const key = formatDate(date);
                  return (
                    <div
                      role="gridcell"
                      aria-selected={key === value}
                      key={column}
                    >
                      <button
                        ref={day === cursor.getDate() ? focusedDay : undefined}
                        type="button"
                        tabIndex={day === cursor.getDate() ? 0 : -1}
                        aria-label={dateFormatter.format(date)}
                        aria-current={key === today ? "date" : undefined}
                        className={cn(
                          "h-10 w-full rounded-md text-sm outline-hidden focus:ring-2 focus:ring-focus focus:ring-offset-1 focus:ring-offset-surface",
                          key === value
                            ? "bg-brand text-on-brand"
                            : "hover:bg-tint",
                          key === today &&
                            key !== value &&
                            "font-semibold text-brand",
                        )}
                        onFocus={() => {
                          if (day !== cursor.getDate()) setCursor(date);
                        }}
                        onKeyDown={onDayKeyDown}
                        onClick={() => choose(key)}
                      >
                        {day}
                      </button>
                    </div>
                  );
                })}
              </div>
            ))}
          </div>
          <div className="mt-4 flex justify-between gap-2 border-t border-border pt-4">
            <Button variant="ghost" size="sm" onClick={() => choose("")}>
              {t("datePicker.clear")}
            </Button>
            <Button variant="outline" size="sm" onClick={() => choose(today)}>
              {t("datePicker.today")}
            </Button>
          </div>
          <DialogPrimitive.Close
            aria-label={t("actions.close")}
            className="absolute right-4 top-4 rounded-md p-1.5 text-subtle hover:text-foreground focus:outline-hidden focus:ring-2 focus:ring-focus"
          >
            <X size={18} />
          </DialogPrimitive.Close>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}
