import React from "react";

import { cn } from "../../lib/cn";

type ButtonVariant = "solid" | "outline" | "ghost" | "icon";
type ButtonColor = "default" | "danger" | "warning" | "success";
type ButtonSize = "sm" | "md" | "lg";

export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  color?: ButtonColor;
  size?: ButtonSize;
  ref?: React.Ref<HTMLButtonElement>;
}

const variantClasses: Record<ButtonVariant, string> = {
  solid: "text-surface shadow-ring-brand",
  outline: "border border-border bg-transparent hover:bg-canvas",
  ghost: "bg-transparent hover:underline",
  icon: "bg-transparent hover:bg-canvas",
};

const solidColorClasses: Record<ButtonColor, string> = {
  default: "bg-brand hover:bg-accent shadow-ring-brand",
  danger: "bg-error hover:opacity-90 shadow-ring-error",
  warning: "bg-warning hover:opacity-90 shadow-ring-warning",
  success: "bg-success hover:opacity-90 shadow-ring-success",
};

const textColorClasses: Record<ButtonColor, string> = {
  default: "text-foreground",
  danger: "text-error",
  warning: "text-warning",
  success: "text-success",
};

const sizeClasses: Record<ButtonSize, string> = {
  sm: "px-3 py-1.5 text-sm",
  md: "px-4 py-2 text-sm",
  lg: "px-6 py-2.5 text-base",
};

const iconSizeClasses: Record<ButtonSize, string> = {
  sm: "p-1.5",
  md: "p-2",
  lg: "p-2.5",
};

export const Button: React.FC<ButtonProps> = ({
  variant = "solid",
  color = "default",
  size = "md",
  className,
  disabled,
  ref,
  ...props
}) => {
  const isIcon = variant === "icon";

  const classes = cn(
    "inline-flex items-center justify-center gap-2 rounded-md font-sans font-medium transition-colors focus:outline-hidden focus:ring-2 focus:ring-focus focus:ring-offset-1 disabled:opacity-50 disabled:pointer-events-none cursor-pointer",
    isIcon ? iconSizeClasses[size] : sizeClasses[size],
    variant === "solid" ? solidColorClasses[color] : variantClasses[variant],
    variant !== "solid" && variant !== "icon" ? textColorClasses[color] : "",
    variant === "icon" ? textColorClasses[color] : "",
    className,
  );

  return (
    <button ref={ref} className={classes} disabled={disabled} {...props} />
  );
};
