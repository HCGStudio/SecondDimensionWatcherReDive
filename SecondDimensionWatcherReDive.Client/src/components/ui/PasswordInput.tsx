import React from "react";
import { useTranslation } from "react-i18next";

import { Eye, EyeOff } from "lucide-react";

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
        onClick={() => setVisible((v) => !v)}
        aria-label={visible ? t("passwordInput.hide") : t("passwordInput.show")}
        className="absolute right-2 top-1/2 -translate-y-1/2 rounded-md p-1 text-subtle transition-colors hover:text-foreground focus:outline-hidden focus:ring-2 focus:ring-focus"
      >
        {visible ? <EyeOff size={16} /> : <Eye size={16} />}
      </button>
    </div>
  );
};
