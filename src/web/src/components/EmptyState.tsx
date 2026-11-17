import React from "react";
import { Button } from "./Button";

export interface EmptyStateProps {
  icon?: React.ReactNode;
  title: string;
  description?: string;
  action?: React.ReactNode;
  className?: string;
}

export function EmptyState({
  icon = "📭",
  title,
  description,
  action,
  className = "",
}: EmptyStateProps) {
  return (
    <div className={`erp-empty-state ${className}`}>
      <div className="erp-empty-icon">{icon}</div>
      <h3 className="erp-empty-title">{title}</h3>
      {description && <p className="erp-empty-desc">{description}</p>}
      {action && <div>{action}</div>}
    </div>
  );
}

export interface LoadingStateProps {
  message?: string;
  className?: string;
}

export function LoadingState({
  message = "Loading data...",
  className = "",
}: LoadingStateProps) {
  return (
    <div className={`erp-empty-state ${className}`}>
      <div
        className="erp-spinner"
        style={{
          width: 32,
          height: 32,
          borderWidth: 3,
          borderColor: "var(--border-strong)",
          borderTopColor: "var(--primary)",
          marginBottom: 16,
        }}
      />
      <p style={{ color: "var(--text-muted)", fontSize: "var(--font-size-sm)" }}>{message}</p>
    </div>
  );
}

export interface ErrorStateProps {
  title?: string;
  message: string;
  onRetry?: () => void;
  className?: string;
}

export function ErrorState({
  title = "Something went wrong",
  message,
  onRetry,
  className = "",
}: ErrorStateProps) {
  return (
    <div className={`erp-empty-state ${className}`}>
      <div className="erp-empty-icon" style={{ color: "var(--danger-500)" }}>
        ⚠️
      </div>
      <h3 className="erp-empty-title">{title}</h3>
      <p className="erp-empty-desc">{message}</p>
      {onRetry && (
        <Button variant="outline" size="sm" onClick={onRetry}>
          Try Again
        </Button>
      )}
    </div>
  );
}
