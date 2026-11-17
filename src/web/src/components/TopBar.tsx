import React from "react";

export type ApiStatus = "online" | "offline" | "checking";

export interface TopBarProps {
  companyName?: string;
  branchName?: string;
  apiStatus: ApiStatus;
  isDark: boolean;
  onToggleTheme: () => void;
  language: string;
  onLanguageChange: (lang: string) => void;
  onOpenMobileSidebar: () => void;
  onSearch?: (term: string) => void;
  userName?: string;
}

export function TopBar({
  companyName = "Default Company",
  branchName,
  apiStatus,
  isDark,
  onToggleTheme,
  language,
  onLanguageChange,
  onOpenMobileSidebar,
  onSearch,
  userName = "Admin",
}: TopBarProps) {
  return (
    <header className="erp-topbar">
      <div className="erp-topbar-left">
        <button
          type="button"
          className="erp-topbar-mobile-toggle"
          onClick={onOpenMobileSidebar}
          aria-label="Open navigation menu"
        >
          ☰
        </button>

        <div className="erp-context-badge">
          <span style={{ fontSize: 13 }}>🏢</span>
          <span style={{ fontWeight: 600, color: "var(--text-primary)" }}>{companyName}</span>
          {branchName && (
            <>
              <span style={{ color: "var(--border-strong)" }}>•</span>
              <span style={{ color: "var(--text-muted)" }}>{branchName}</span>
            </>
          )}
        </div>

        {onSearch && (
          <div className="erp-topbar-search">
            <span className="erp-topbar-search-icon">🔍</span>
            <input
              type="text"
              placeholder="Search ERP modules, orders, products..."
              onChange={(e) => onSearch(e.target.value)}
              aria-label="Global search"
            />
          </div>
        )}
      </div>

      <div className="erp-topbar-right">
        {/* Language selector */}
        <select
          value={language}
          onChange={(e) => onLanguageChange(e.target.value)}
          className="erp-select"
          style={{ width: "auto", height: 36, padding: "0 8px", fontSize: 12 }}
          aria-label="Language selector"
        >
          <option value="en">English (US)</option>
          <option value="ur">اردو (Urdu)</option>
          <option value="ar">العربية (Arabic RTL)</option>
        </select>

        {/* Theme toggle */}
        <button
          type="button"
          className="erp-action-btn-icon"
          onClick={onToggleTheme}
          aria-label={isDark ? "Switch to light mode" : "Switch to dark mode"}
          title={isDark ? "Light Mode" : "Dark Mode"}
        >
          {isDark ? "☀️" : "🌙"}
        </button>

        {/* Status pill */}
        <div className={`erp-status-pill ${apiStatus}`}>
          <span className="erp-status-dot" />
          <span>{apiStatus === "online" ? "API Online" : apiStatus === "offline" ? "API Offline" : "Checking"}</span>
        </div>

        {/* User avatar */}
        <div
          style={{
            width: 34,
            height: 34,
            borderRadius: "50%",
            background: "linear-gradient(135deg, #2563eb, #1d4ed8)",
            color: "#ffffff",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            fontWeight: 700,
            fontSize: 13,
            cursor: "pointer",
            boxShadow: "0 1px 3px rgba(0,0,0,0.1)",
          }}
          title={userName}
        >
          {userName.charAt(0).toUpperCase()}
        </div>
      </div>
    </header>
  );
}
