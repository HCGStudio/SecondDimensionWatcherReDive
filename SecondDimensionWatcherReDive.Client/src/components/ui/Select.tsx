import React from "react";

import * as SelectPrimitive from "@radix-ui/react-select";

import { Check, ChevronDown, ChevronUp } from "lucide-react";

import { cn } from "../../lib/cn";

// Radix reserves an empty value for its placeholder. Encode every value so that
// empty options remain selectable without colliding with application values.
const encodeValue = (value: string) => `value:${value}`;
const decodeValue = (value: string) => value.slice("value:".length);

export interface SelectProps extends Omit<
  React.ComponentPropsWithoutRef<typeof SelectPrimitive.Trigger>,
  "children" | "defaultValue" | "value" | "onChange" | "name"
> {
  children: React.ReactNode;
  value?: string;
  defaultValue?: string;
  onValueChange?: (value: string) => void;
  placeholder?: string;
  required?: boolean;
}

export const Select = React.forwardRef<HTMLButtonElement, SelectProps>(
  (
    {
      children,
      value,
      defaultValue,
      onValueChange,
      placeholder,
      disabled,
      required,
      className,
      ...props
    },
    ref,
  ) => (
    <SelectPrimitive.Root
      value={value === undefined ? undefined : encodeValue(value)}
      defaultValue={
        defaultValue === undefined ? undefined : encodeValue(defaultValue)
      }
      onValueChange={(nextValue) => onValueChange?.(decodeValue(nextValue))}
      disabled={disabled}
      required={required}
    >
      <SelectPrimitive.Trigger
        ref={ref}
        className={cn(
          "flex w-full min-w-0 max-w-full items-center justify-between gap-2 rounded-lg border border-border bg-surface px-3 py-2 text-left text-sm text-foreground transition-colors",
          "focus:border-focus focus:outline-hidden focus:ring-2 focus:ring-focus disabled:pointer-events-none disabled:opacity-50",
          className,
        )}
        {...props}
      >
        <span className="min-w-0 flex-1 truncate">
          <SelectPrimitive.Value placeholder={placeholder} />
        </span>
        <SelectPrimitive.Icon asChild>
          <ChevronDown
            size={15}
            className="shrink-0 text-subtle"
            aria-hidden="true"
          />
        </SelectPrimitive.Icon>
      </SelectPrimitive.Trigger>
      <SelectPrimitive.Portal>
        <SelectPrimitive.Content
          position="popper"
          sideOffset={5}
          collisionPadding={12}
          className="z-[100] max-h-[var(--radix-select-content-available-height)] min-w-[var(--radix-select-trigger-width)] max-w-[calc(100vw-1.5rem)] overflow-hidden rounded-lg border border-border bg-surface text-foreground shadow-whisper"
        >
          <SelectPrimitive.ScrollUpButton className="flex h-6 items-center justify-center bg-surface text-subtle">
            <ChevronUp size={15} aria-hidden="true" />
          </SelectPrimitive.ScrollUpButton>
          <SelectPrimitive.Viewport className="p-1">
            {children}
          </SelectPrimitive.Viewport>
          <SelectPrimitive.ScrollDownButton className="flex h-6 items-center justify-center bg-surface text-subtle">
            <ChevronDown size={15} aria-hidden="true" />
          </SelectPrimitive.ScrollDownButton>
        </SelectPrimitive.Content>
      </SelectPrimitive.Portal>
    </SelectPrimitive.Root>
  ),
);
Select.displayName = "Select";

export const SelectItem = React.forwardRef<
  React.ComponentRef<typeof SelectPrimitive.Item>,
  React.ComponentPropsWithoutRef<typeof SelectPrimitive.Item>
>(({ value, children, className, ...props }, ref) => (
  <SelectPrimitive.Item
    ref={ref}
    value={encodeValue(value)}
    className={cn(
      "relative flex cursor-pointer select-none items-center rounded-md py-2 pl-8 pr-3 text-sm outline-hidden transition-colors data-[highlighted]:bg-tint data-[disabled]:pointer-events-none data-[disabled]:opacity-50",
      className,
    )}
    {...props}
  >
    <SelectPrimitive.ItemIndicator className="absolute left-2 inline-flex items-center text-brand">
      <Check size={15} aria-hidden="true" />
    </SelectPrimitive.ItemIndicator>
    <span className="min-w-0 whitespace-normal break-words">
      <SelectPrimitive.ItemText>{children}</SelectPrimitive.ItemText>
    </span>
  </SelectPrimitive.Item>
));
SelectItem.displayName = "SelectItem";
