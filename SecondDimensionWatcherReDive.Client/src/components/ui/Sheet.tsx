import React from "react";
import { useTranslation } from "react-i18next";

import * as DialogPrimitive from "@radix-ui/react-dialog";

import { X } from "lucide-react";

import { cn } from "../../lib/cn";

export const Sheet = DialogPrimitive.Root;
export const SheetTrigger = DialogPrimitive.Trigger;

type SheetContentProps = React.ComponentPropsWithoutRef<
  typeof DialogPrimitive.Content
> & {
  side?: "left" | "right";
};

export const SheetContent = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Content>,
  SheetContentProps
>(({ className, children, side = "right", ...props }, ref) => {
  const { t } = useTranslation();
  return (
    <DialogPrimitive.Portal>
      <DialogPrimitive.Overlay className="fixed inset-0 z-40 bg-black/30 data-[state=open]:animate-in data-[state=closed]:animate-out" />
      <DialogPrimitive.Content
        ref={ref}
        className={cn(
          "fixed top-0 z-50 flex h-full w-full max-w-lg flex-col bg-surface shadow-whisper",
          side === "right"
            ? "right-0 border-l border-border data-[state=open]:animate-slide-in-right data-[state=closed]:animate-slide-out-right"
            : "left-0 border-r border-border data-[state=open]:animate-slide-in-left data-[state=closed]:animate-slide-out-left",
          className,
        )}
        {...props}
      >
        {children}
        <DialogPrimitive.Close
          type="button"
          className="absolute right-4 top-4 rounded-md p-1 text-subtle hover:text-foreground transition-colors focus:outline-hidden focus:ring-2 focus:ring-focus"
        >
          <X size={18} />
          <span className="sr-only">{t("actions.close")}</span>
        </DialogPrimitive.Close>
      </DialogPrimitive.Content>
    </DialogPrimitive.Portal>
  );
});

SheetContent.displayName = "SheetContent";

export const SheetHeader: React.FC<React.HTMLAttributes<HTMLDivElement>> = ({
  className,
  ...props
}) => (
  <div
    className={cn("border-b border-border px-4 py-4 sm:px-6", className)}
    {...props}
  />
);

export const SheetBody: React.FC<React.HTMLAttributes<HTMLDivElement>> = ({
  className,
  ...props
}) => (
  <div
    className={cn("flex-1 overflow-y-auto px-4 py-4 sm:px-6", className)}
    {...props}
  />
);

export const SheetTitle = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Title>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Title>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Title
    ref={ref}
    className={cn("font-serif text-lg font-medium leading-heading", className)}
    {...props}
  />
));

SheetTitle.displayName = "SheetTitle";
