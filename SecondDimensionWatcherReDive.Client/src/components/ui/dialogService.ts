export interface ConfirmDialogOptions {
  title?: string;
  confirmLabel?: string;
  cancelLabel?: string;
  destructive?: boolean;
}

export interface PromptDialogOptions extends ConfirmDialogOptions {
  defaultValue?: string;
  inputType?: "text" | "password";
  inputMode?: "text" | "numeric" | "decimal" | "email" | "url" | "tel";
  autoComplete?: string;
}

export interface DialogRequest {
  id: number;
  kind: "confirm" | "prompt";
  message: string;
  options: PromptDialogOptions;
  restoreFocus: HTMLElement | null;
  resolve: (value: boolean | string | null) => void;
}

const listeners = new Set<() => void>();
const queue: DialogRequest[] = [];
let active: DialogRequest | null = null;
let pendingRestoreFocus: HTMLElement | null = null;
let nextId = 0;
let scheduled = false;

const notify = () => listeners.forEach((listener) => listener());

function showNextDialog() {
  if (active || scheduled || queue.length === 0) return;
  scheduled = true;
  // Radix restores dropdown focus in a timer after unmount. Give both the
  // triggering event and that cleanup a turn before opening the next modal.
  setTimeout(() => {
    setTimeout(() => {
      scheduled = false;
      if (active || queue.length === 0) return;
      active = queue.shift()!;
      active.restoreFocus =
        document.activeElement instanceof HTMLElement &&
        document.activeElement !== document.body
          ? document.activeElement
          : active.restoreFocus;
      notify();
    }, 0);
  }, 0);
}

function enqueueDialog(
  kind: DialogRequest["kind"],
  message: string,
  options: PromptDialogOptions,
) {
  return new Promise<boolean | string | null>((resolve) => {
    queue.push({
      id: nextId++,
      kind,
      message,
      options,
      restoreFocus:
        active?.restoreFocus ??
        (document.activeElement instanceof HTMLElement &&
        document.activeElement !== document.body
          ? document.activeElement
          : pendingRestoreFocus),
      resolve,
    });
    showNextDialog();
  });
}

export async function confirmDialog(
  message: string,
  options: ConfirmDialogOptions = {},
): Promise<boolean> {
  return (await enqueueDialog("confirm", message, options)) === true;
}

export async function promptDialog(
  message: string,
  options: PromptDialogOptions = {},
): Promise<string | null> {
  const result = await enqueueDialog("prompt", message, options);
  return typeof result === "string" ? result : null;
}

export const getCurrentDialog = () => active;

export function subscribeDialogs(listener: () => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
    queueMicrotask(() => {
      // StrictMode can resubscribe immediately; only cancel on a real unmount.
      if (listeners.size > 0) return;
      const pending = active ? [active, ...queue] : [...queue];
      active = null;
      pendingRestoreFocus = null;
      queue.length = 0;
      pending.forEach((request) => request.resolve(null));
    });
  };
}

export function finishDialog(id: number, value: boolean | string | null) {
  if (active?.id !== id) return;
  const request = active;
  active = null;
  pendingRestoreFocus = request.restoreFocus;
  notify();
  request.resolve(value);
  // The caller may need to clear its busy state before its button can receive
  // focus. A following prompt inherits this target and must keep its own focus.
  setTimeout(() => {
    setTimeout(() => {
      if (active || queue.length > 0) return;
      if (pendingRestoreFocus?.isConnected) pendingRestoreFocus.focus();
      pendingRestoreFocus = null;
    }, 0);
  }, 0);
  showNextDialog();
}
