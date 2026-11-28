import React, { useState, useEffect } from "react";
import { Sidebar, NavItem } from "./Sidebar";
import { TopBar, ApiStatus } from "./TopBar";

export interface AppShellProps {
  children: React.ReactNode;
  navItems: NavItem[];
  activeNavId: string;
  onSelectNav: (id: string) => void;
  companyName?: string;
  branchName?: string;
  apiStatus: ApiStatus;
  userName?: string;
  userEmail?: string;
  onSignOut?: () => void;
  onSearch?: (term: string) => void;
}

export function AppShell({
  children,
  navItems,
  activeNavId,
  onSelectNav,
  companyName,
  branchName,
  apiStatus,
  userName,
  userEmail,
  onSignOut,
  onSearch,
}: AppShellProps) {
  const [collapsed, setCollapsed] = useState<boolean>(() => {
    try {
      return localStorage.getItem("erp_sidebar_collapsed") === "true";
    } catch {
      return false;
    }
  });

  const [mobileOpen, setMobileOpen] = useState(false);

  const [isDark, setIsDark] = useState<boolean>(() => {
    try {
      return localStorage.getItem("erp_theme") === "dark";
    } catch {
      return false;
    }
  });

  const [language, setLanguage] = useState<string>(() => {
    try {
      return localStorage.getItem("erp_language") || "en";
    } catch {
      return "en";
    }
  });

  // Apply theme to document element
  useEffect(() => {
    if (isDark) {
      document.documentElement.setAttribute("data-theme", "dark");
    } else {
      document.documentElement.removeAttribute("data-theme");
    }
    try {
      localStorage.setItem("erp_theme", isDark ? "dark" : "light");
    } catch {
      // ignore
    }
  }, [isDark]);

  // Apply RTL direction when Arabic is selected
  useEffect(() => {
    if (language === "ar") {
      document.documentElement.setAttribute("dir", "rtl");
    } else {
      document.documentElement.removeAttribute("dir");
    }
    try {
      localStorage.setItem("erp_language", language);
    } catch {
      // ignore
    }
  }, [language]);

  const toggleCollapse = () => {
    setCollapsed((prev) => {
      const next = !prev;
      try {
        localStorage.setItem("erp_sidebar_collapsed", String(next));
      } catch {
        // ignore
      }
      return next;
    });
  };

  const toggleTheme = () => {
    setIsDark((prev) => !prev);
  };

  return (
    <div className="erp-app-shell">
      <Sidebar
        items={navItems}
        activeId={activeNavId}
        onSelect={onSelectNav}
        collapsed={collapsed}
        onToggleCollapse={toggleCollapse}
        mobileOpen={mobileOpen}
        onCloseMobile={() => setMobileOpen(false)}
        userName={userName}
        userEmail={userEmail}
        onSignOut={onSignOut}
      />

      <div className={`erp-main-container ${collapsed ? "sidebar-collapsed" : ""}`}>
        <TopBar
          companyName={companyName}
          branchName={branchName}
          apiStatus={apiStatus}
          isDark={isDark}
          onToggleTheme={toggleTheme}
          language={language}
          onLanguageChange={setLanguage}
          onOpenMobileSidebar={() => setMobileOpen(true)}
          onSearch={onSearch}
          userName={userName}
        />

        <main className="erp-content-area">{children}</main>
      </div>
    </div>
  );
}
