import React, { forwardRef } from "react";

export type ButtonVariant = "primary" | "secondary" | "outline" | "ghost" | "danger" | "success";
export type ButtonSize = "xs" | "sm" | "md" | "lg";

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  loading?: boolean;
  icon?: React.ReactNode;
  iconEnd?: React.ReactNode;
  fullWidth?: boolean;
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  {
    children,
    variant = "primary",
    size = "md",
    loading = false,
    icon,
    iconEnd,
    fullWidth = false,
    className = "",
    disabled,
    ...props
  },
  ref
) {
  const classes = [
    "erp-btn",
    `erp-btn-${variant}`,
    `erp-btn-${size}`,
    fullWidth ? "erp-btn-block" : "",
    className,
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <button ref={ref} className={classes} disabled={disabled || loading} {...props}>
      {loading ? (
        <span className="erp-spinner" aria-hidden="true" />
      ) : icon ? (
        <span className="btn-icon-start">{icon}</span>
      ) : null}
      <span>{children}</span>
      {!loading && iconEnd && <span className="btn-icon-end">{iconEnd}</span>}
    </button>
  );
});
