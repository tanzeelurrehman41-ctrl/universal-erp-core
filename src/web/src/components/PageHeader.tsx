import React from "react";

export interface Breadcrumb {
  label: string;
  onClick?: () => void;
  active?: boolean;
}

export interface PageHeaderProps {
  title: React.ReactNode;
  subtitle?: React.ReactNode;
  breadcrumbs?: Breadcrumb[];
  actions?: React.ReactNode;
  className?: string;
}

export function PageHeader({
  title,
  subtitle,
  breadcrumbs,
  actions,
  className = "",
}: PageHeaderProps) {
  return (
    <div className={`erp-page-header ${className}`}>
      <div>
        {breadcrumbs && breadcrumbs.length > 0 && (
          <nav aria-label="Breadcrumb" className="erp-breadcrumbs">
            {breadcrumbs.map((crumb, idx) => (
              <React.Fragment key={idx}>
                {idx > 0 && <span>/</span>}
                {crumb.onClick && !crumb.active ? (
                  <button
                    type="button"
                    className="erp-breadcrumb-item"
                    onClick={crumb.onClick}
                    style={{ background: "none", border: "none", padding: 0, font: "inherit" }}
                  >
                    {crumb.label}
                  </button>
                ) : (
                  <span className={`erp-breadcrumb-item ${crumb.active ? "active" : ""}`}>
                    {crumb.label}
                  </span>
                )}
              </React.Fragment>
            ))}
          </nav>
        )}
        <h1 className="erp-page-title">{title}</h1>
        {subtitle && <p className="erp-page-subtitle">{subtitle}</p>}
      </div>
      {actions && <div className="erp-page-actions">{actions}</div>}
    </div>
  );
}
