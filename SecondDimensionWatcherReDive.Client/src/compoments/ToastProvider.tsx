import { EuiGlobalToastList, Toast } from "@elastic/eui";
import React from "react";

interface ToastContextValue {
  addToast: (toast: Omit<Toast, "id">) => void;
}

const ToastContext = React.createContext<ToastContextValue>({
  addToast: () => {},
});

export const useToast = () => React.useContext(ToastContext);

export const ToastProvider: React.FC<React.PropsWithChildren> = ({
  children,
}) => {
  const [toasts, setToasts] = React.useState<Toast[]>([]);
  const idRef = React.useRef(0);

  const addToast = React.useCallback((toast: Omit<Toast, "id">) => {
    const id = String(idRef.current++);
    setToasts((prev) => [...prev, { ...toast, id }]);
  }, []);

  const removeToast = React.useCallback((removed: Toast) => {
    setToasts((prev) => prev.filter((t) => t.id !== removed.id));
  }, []);

  return (
    <ToastContext.Provider value={{ addToast }}>
      {children}
      <EuiGlobalToastList
        toasts={toasts}
        dismissToast={removeToast}
        toastLifeTimeMs={5000}
      />
    </ToastContext.Provider>
  );
};
