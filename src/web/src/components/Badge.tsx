import React from "react";

export type BadgeVariant = "default" | "primary" | "success" | "warning" | "danger" | "info";

export interface BadgeProps {
  variant?: BadgeVariant;
  children: React.ReactNode;
  dot?: boolean;
  className?: string;
}

export function Badge({
  variant = "default",
  children,
  dot = false,
  className = "",
}: BadgeProps) {
  return (
    <span className={`erp-badge erp-badge-${variant} ${className}`}>
      {dot && <span className="erp-badge-dot" />}
      <span>{children}</span>
    </span>
  );
}
