import React from "react";

export interface KPICardProps {
  title: string;
  value: React.ReactNode;
  icon?: React.ReactNode;
  color?: "blue" | "green" | "amber" | "red" | "purple";
  change?: string;
  changeType?: "up" | "down" | "neutral";
  changePeriod?: string;
  className?: string;
  onClick?: () => void;
}

export function KPICard({
  title,
  value,
  icon,
  color = "blue",
  change,
  changeType = "neutral",
  changePeriod = "vs last period",
  className = "",
  onClick,
}: KPICardProps) {
  return (
    <div
      className={`erp-kpi-card ${className}`}
      onClick={onClick}
      style={onClick ? { cursor: "pointer" } : undefined}
    >
      <div className="erp-kpi-header">
        <span className="erp-kpi-title">{title}</span>
        {icon && <div className={`erp-kpi-icon ${color}`}>{icon}</div>}
      </div>
      <div className="erp-kpi-value">{value}</div>
      {(change || changePeriod) && (
        <div className="erp-kpi-footer">
          {change && (
            <span className={`erp-kpi-trend ${changeType}`}>
              {changeType === "up" ? "↑" : changeType === "down" ? "↓" : "•"} {change}
            </span>
          )}
          <span>{changePeriod}</span>
        </div>
      )}
    </div>
  );
}
