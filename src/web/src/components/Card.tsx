import React from "react";

export interface CardProps {
  title?: React.ReactNode;
  subtitle?: React.ReactNode;
  icon?: React.ReactNode;
  actions?: React.ReactNode;
  footer?: React.ReactNode;
  children: React.ReactNode;
  className?: string;
  bodyClassName?: string;
}

export function Card({
  title,
  subtitle,
  icon,
  actions,
  footer,
  children,
  className = "",
  bodyClassName = "",
}: CardProps) {
  const hasHeader = Boolean(title || subtitle || icon || actions);

  return (
    <div className={`erp-card ${className}`}>
      {hasHeader && (
        <div className="erp-card-header">
          <div className="erp-card-title-group">
            {icon && <div className="erp-card-icon">{icon}</div>}
            <div>
              {title && <h3 className="erp-card-title">{title}</h3>}
              {subtitle && <p className="erp-card-subtitle">{subtitle}</p>}
            </div>
          </div>
          {actions && <div className="erp-card-actions">{actions}</div>}
        </div>
      )}
      <div className={`erp-card-body ${bodyClassName}`}>{children}</div>
      {footer && <div className="erp-card-footer">{footer}</div>}
    </div>
  );
}
