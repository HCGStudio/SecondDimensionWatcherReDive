import { Eye, EyeOff } from "lucide-react";
import React from "react";
import { useTranslation } from "react-i18next";

import { cn } from "../../lib/cn";
import { Input, type InputProps } from "./Input";

export const PasswordInput: React.FC<InputProps> = ({
  className,
  ref,
  ...props
}) => {
  const { t } = useTranslation();
  const [visible, setVisible] = React.useState(false);

  return (
    <div className="relative">
      <Input
        ref={ref}
        type={visible ? "text" : "password"}
        className={cn("pr-10", className)}
        {...props}
      />
      <button
        type="button"
        className="absolute right-3 top-1/2 -translate-y-1/2 text-subtle hover:text-foreground transition-colors"
        onClick={() => setVisible((v) => !v)}
        tabIndex={-1}
        aria-label={visible ? t("passwordInput.hide") : t("passwordInput.show")}
      >
        {visible ? <EyeOff size={16} /> : <Eye size={16} />}
      </button>
    </div>
  );
};
