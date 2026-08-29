import React from "react";
import { useTranslation } from "react-i18next";

import * as ToastPrimitive from "@radix-ui/react-toast";

import { X } from "lucide-react";

import { cn } from "../lib/cn";

export interface Toast {
  id: string;
  title: string;
  color?: "success" | "danger" | "warning" | "primary";
  text?: string;
}

interface ToastContextValue {
  addToast: (toast: Omit<Toast, "id">) => void;
}

const ToastContext = React.createContext<ToastContextValue>({
  addToast: () => {},
});

export const useToast = () => React.useContext(ToastContext);

const colorBorderClasses: Record<string, string> = {
  success: "border-l-4 border-l-success",
  danger: "border-l-4 border-l-error",
  warning: "border-l-4 border-l-warning",
  primary: "border-l-4 border-l-brand",
};

export const ToastProvider: React.FC<React.PropsWithChildren> = ({
  children,
}) => {
  const { t } = useTranslation("common");
  const [toasts, setToasts] = React.useState<Toast[]>([]);
  const idRef = React.useRef(0);

  const addToast = React.useCallback((toast: Omit<Toast, "id">) => {
    const id = String(idRef.current++);
    setToasts((prev) => [...prev, { ...toast, id }]);
  }, []);

  const removeToast = React.useCallback((id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  return (
    <ToastContext.Provider value={{ addToast }}>
      <ToastPrimitive.Provider
        swipeDirection="right"
        label={t("notifications.label")}
      >
        {children}
        {toasts.map((toast) => (
          <ToastPrimitive.Root
            key={toast.id}
            className={cn(
              "rounded-md border border-border bg-surface px-4 py-3 shadow-whisper",
              "data-[state=open]:animate-toast-in data-[state=closed]:animate-toast-out",
              colorBorderClasses[toast.color ?? "primary"],
            )}
            duration={5000}
            type={toast.color === "danger" ? "foreground" : "background"}
            onOpenChange={(open) => {
              if (!open) removeToast(toast.id);
            }}
          >
            <div className="flex items-start justify-between gap-3">
              <div>
                <ToastPrimitive.Title className="text-sm font-medium text-foreground">
                  {toast.title}
                </ToastPrimitive.Title>
                {toast.text ? (
                  <ToastPrimitive.Description className="mt-1 text-sm text-muted">
                    {toast.text}
                  </ToastPrimitive.Description>
                ) : null}
              </div>
              <ToastPrimitive.Close
                aria-label={t("notifications.dismiss")}
                className="shrink-0 rounded-md p-1 text-subtle hover:text-foreground transition-colors focus:outline-hidden focus:ring-2 focus:ring-focus"
              >
                <X size={14} />
              </ToastPrimitive.Close>
            </div>
          </ToastPrimitive.Root>
        ))}
        <ToastPrimitive.Viewport className="fixed bottom-4 left-4 right-4 z-50 flex flex-col gap-2 sm:left-auto sm:w-80" />
      </ToastPrimitive.Provider>
    </ToastContext.Provider>
  );
};
