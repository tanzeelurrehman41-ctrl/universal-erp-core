import React, { forwardRef } from "react";

export interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  hint?: string;
  error?: string;
  icon?: React.ReactNode;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(function Input(
  { label, hint, error, icon, className = "", id, required, ...props },
  ref
) {
  const inputId = id || (label ? label.toLowerCase().replace(/\s+/g, "-") : undefined);

  return (
    <div className="erp-form-group">
      {label && (
        <label htmlFor={inputId} className="erp-form-label">
          {label}
          {required && <span className="erp-form-required">*</span>}
        </label>
      )}
      <div className="erp-input-wrapper">
        {icon && <span style={{ position: "absolute", left: 10, color: "var(--text-muted)", pointerEvents: "none" }}>{icon}</span>}
        <input
          ref={ref}
          id={inputId}
          className={`erp-input ${error ? "error" : ""} ${className}`}
          style={icon ? { paddingLeft: 34 } : undefined}
          required={required}
          {...props}
        />
      </div>
      {hint && !error && <span className="erp-form-hint">{hint}</span>}
      {error && <span className="erp-form-error">{error}</span>}
    </div>
  );
});

export interface SelectOption {
  value: string | number;
  label: string;
  disabled?: boolean;
}

export interface SelectProps extends React.SelectHTMLAttributes<HTMLSelectElement> {
  label?: string;
  hint?: string;
  error?: string;
  options?: SelectOption[];
}

export const Select = forwardRef<HTMLSelectElement, SelectProps>(function Select(
  { label, hint, error, options, children, className = "", id, required, ...props },
  ref
) {
  const selectId = id || (label ? label.toLowerCase().replace(/\s+/g, "-") : undefined);

  return (
    <div className="erp-form-group">
      {label && (
        <label htmlFor={selectId} className="erp-form-label">
          {label}
          {required && <span className="erp-form-required">*</span>}
        </label>
      )}
      <select
        ref={ref}
        id={selectId}
        className={`erp-select ${error ? "error" : ""} ${className}`}
        required={required}
        {...props}
      >
        {options
          ? options.map((opt) => (
              <option key={opt.value} value={opt.value} disabled={opt.disabled}>
                {opt.label}
              </option>
            ))
          : children}
      </select>
      {hint && !error && <span className="erp-form-hint">{hint}</span>}
      {error && <span className="erp-form-error">{error}</span>}
    </div>
  );
});

export interface TextareaProps extends React.TextareaHTMLAttributes<HTMLTextAreaElement> {
  label?: string;
  hint?: string;
  error?: string;
}

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(function Textarea(
  { label, hint, error, className = "", id, required, ...props },
  ref
) {
  const textareaId = id || (label ? label.toLowerCase().replace(/\s+/g, "-") : undefined);

  return (
    <div className="erp-form-group">
      {label && (
        <label htmlFor={textareaId} className="erp-form-label">
          {label}
          {required && <span className="erp-form-required">*</span>}
        </label>
      )}
      <textarea
        ref={ref}
        id={textareaId}
        className={`erp-textarea ${error ? "error" : ""} ${className}`}
        required={required}
        {...props}
      />
      {hint && !error && <span className="erp-form-hint">{hint}</span>}
      {error && <span className="erp-form-error">{error}</span>}
    </div>
  );
});
