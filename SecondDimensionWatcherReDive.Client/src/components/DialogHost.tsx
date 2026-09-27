import React from "react";
import { useTranslation } from "react-i18next";

import { useToast } from "./ToastProvider";
import { Button } from "./ui/Button";
import { Spinner } from "./ui/Spinner";
import {
  type DialogRequest,
  finishDialog,
  getCurrentDialog,
  subscribeDialogs,
} from "./ui/dialogService";

type DialogComponent = React.ComponentType<{ request: DialogRequest }>;
let pendingImport: Promise<{ default: DialogComponent }> | null = null;

function loadDialogWindow() {
  pendingImport ??= import("./DialogWindow").catch((error: unknown) => {
    pendingImport = null;
    throw error;
  });
  return pendingImport;
}

export function DialogHost() {
  const { t } = useTranslation(["common", "errors"]);
  const { addToast } = useToast();
  const request = React.useSyncExternalStore(
    subscribeDialogs,
    getCurrentDialog,
  );
  const [DialogWindow, setDialogWindow] =
    React.useState<DialogComponent | null>(null);

  React.useEffect(() => {
    if (!request || DialogWindow) return;
    let current = true;
    const fail = () => {
      if (!current || getCurrentDialog()?.id !== request.id) return;
      addToast({ title: t("errors:loadFailed"), color: "danger" });
      finishDialog(request.id, null);
    };
    // A stalled download must not leave a destructive-action caller waiting.
    const timeout = setTimeout(fail, 15000);
    void loadDialogWindow().then(
      (module) => {
        if (current) setDialogWindow(() => module.default);
        clearTimeout(timeout);
      },
      () => {
        clearTimeout(timeout);
        fail();
      },
    );
    const cancelOnEscape = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      event.preventDefault();
      event.stopPropagation();
      finishDialog(request.id, null);
    };
    document.addEventListener("keydown", cancelOnEscape, true);
    return () => {
      current = false;
      clearTimeout(timeout);
      document.removeEventListener("keydown", cancelOnEscape, true);
    };
  }, [request, DialogWindow, addToast, t]);

  if (!request) return null;
  if (DialogWindow) return <DialogWindow key={request.id} request={request} />;
  return (
    <div
      role="status"
      className="fixed bottom-4 left-4 right-4 z-[80] flex items-center gap-3 rounded-xl border border-border bg-surface p-4 shadow-whisper sm:left-auto sm:max-w-md"
    >
      <Spinner size={20} className="shrink-0" />
      <span className="min-w-0 flex-1 break-words text-sm text-foreground">
        {request.options.title ?? request.message}
      </span>
      <Button
        variant="outline"
        size="sm"
        onClick={() => finishDialog(request.id, null)}
      >
        {request.options.cancelLabel ?? t("common:dialogs.cancel")}
      </Button>
    </div>
  );
}
