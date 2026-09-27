import React from "react";
import { useTranslation } from "react-i18next";

import * as DialogPrimitive from "@radix-ui/react-dialog";

import { CircleAlert, MessageSquareText, X } from "lucide-react";

import { Button } from "./ui/Button";
import { Input } from "./ui/Input";
import { PasswordInput } from "./ui/PasswordInput";
import { type DialogRequest, finishDialog } from "./ui/dialogService";

export default function DialogWindow({ request }: { request: DialogRequest }) {
  const { t } = useTranslation("common");
  const [open, setOpen] = React.useState(true);
  const [value, setValue] = React.useState(request.options.defaultValue ?? "");
  const result = React.useRef<boolean | string | null>(null);
  const closing = React.useRef(false);
  const inputRef = React.useRef<HTMLInputElement>(null);
  const cancelRef = React.useRef<HTMLButtonElement>(null);
  const descriptionId = React.useId();
  const inputId = React.useId();
  const isPrompt = request.kind === "prompt";
  const { options } = request;
  const InputComponent =
    options.inputType === "password" ? PasswordInput : Input;

  const closeDialog = (nextResult: boolean | string | null) => {
    if (closing.current) return;
    closing.current = true;
    result.current = nextResult;
    setOpen(false);
  };

  return (
    <DialogPrimitive.Root
      open={open}
      onOpenChange={(nextOpen) => {
        if (!nextOpen) closeDialog(null);
      }}
    >
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 z-[70] bg-black/40" />
        <DialogPrimitive.Content
          aria-describedby={descriptionId}
          className="fixed left-1/2 top-1/2 z-[80] max-h-[calc(100dvh-2rem)] w-[calc(100vw_-_2rem)] max-w-md -translate-x-1/2 -translate-y-1/2 overflow-y-auto rounded-2xl border border-border bg-surface p-5 text-foreground shadow-whisper sm:p-6"
          onOpenAutoFocus={(event) => {
            event.preventDefault();
            if (isPrompt) {
              inputRef.current?.focus();
              inputRef.current?.select();
            } else {
              cancelRef.current?.focus();
            }
          }}
          onCloseAutoFocus={(event) => {
            event.preventDefault();
            if (!closing.current) return;
            // Resume the caller only after Radix has released the focus trap.
            finishDialog(request.id, result.current);
          }}
        >
          <div
            className={`mb-4 inline-flex rounded-xl p-2.5 ${options.destructive ? "bg-error/10 text-error" : "bg-brand/10 text-brand"}`}
          >
            {options.destructive ? (
              <CircleAlert size={22} aria-hidden="true" />
            ) : (
              <MessageSquareText size={22} aria-hidden="true" />
            )}
          </div>
          <DialogPrimitive.Title className="pr-6 font-sans text-lg font-medium leading-heading">
            {options.title ??
              t(isPrompt ? "dialogs.inputTitle" : "dialogs.confirmTitle")}
          </DialogPrimitive.Title>
          <DialogPrimitive.Description
            id={descriptionId}
            className="mt-2 whitespace-pre-wrap break-words text-sm leading-relaxed text-muted"
          >
            {request.message}
          </DialogPrimitive.Description>
          <form
            onSubmit={(event) => {
              event.preventDefault();
              if (!open) return;
              closeDialog(isPrompt ? value : true);
            }}
          >
            {isPrompt ? (
              <div className="mt-5">
                <label htmlFor={inputId} className="sr-only">
                  {request.message}
                </label>
                <InputComponent
                  id={inputId}
                  ref={inputRef}
                  value={value}
                  onChange={(event) => setValue(event.target.value)}
                  inputMode={options.inputMode}
                  autoComplete={options.autoComplete ?? "off"}
                  className="text-base sm:text-sm"
                  onKeyDown={(event) => {
                    if (event.key === "Enter" && event.nativeEvent.isComposing)
                      event.preventDefault();
                  }}
                />
              </div>
            ) : null}
            <div className="mt-6 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
              <Button
                ref={cancelRef}
                variant="outline"
                onClick={() => closeDialog(null)}
              >
                {options.cancelLabel ?? t("dialogs.cancel")}
              </Button>
              <Button
                type="submit"
                color={options.destructive ? "danger" : "default"}
              >
                {options.confirmLabel ?? t("dialogs.confirm")}
              </Button>
            </div>
          </form>
          <DialogPrimitive.Close
            aria-label={t("actions.close")}
            className="absolute right-4 top-4 rounded-md p-1.5 text-subtle transition-colors hover:text-foreground focus:outline-hidden focus:ring-2 focus:ring-focus"
          >
            <X size={18} />
          </DialogPrimitive.Close>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}
