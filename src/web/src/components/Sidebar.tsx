import React from "react";

export interface NavItem {
  id: string;
  label: string;
  icon: string;
  badge?: string | number;
  group: "overview" | "operations" | "finance" | "organization" | "platform";
}

export interface SidebarProps {
  items: NavItem[];
  activeId: string;
  onSelect: (id: string) => void;
  collapsed: boolean;
  onToggleCollapse: () => void;
  mobileOpen: boolean;
  onCloseMobile: () => void;
  userName?: string;
  userEmail?: string;
  onSignOut?: () => void;
}

const GROUP_LABELS: Record<string, string> = {
  overview: "Executive",
  operations: "Operations",
  finance: "Finance & BI",
  organization: "Organization",
  platform: "Platform",
};

export function Sidebar({
  items,
  activeId,
  onSelect,
  collapsed,
  onToggleCollapse,
  mobileOpen,
  onCloseMobile,
  userName = "Admin User",
  userEmail = "admin@universal-erp.io",
  onSignOut,
}: SidebarProps) {
  // Group items
  const groupedItems = items.reduce<Record<string, NavItem[]>>((acc, item) => {
    if (!acc[item.group]) acc[item.group] = [];
    acc[item.group].push(item);
    return acc;
  }, {});

  const groupsOrder = ["overview", "operations", "finance", "organization", "platform"];

  return (
    <>
      <div
        className={`erp-sidebar-backdrop ${mobileOpen ? "mobile-open" : ""}`}
        onClick={onCloseMobile}
        aria-hidden="true"
      />
      <aside
        className={`erp-sidebar ${collapsed ? "collapsed" : ""} ${mobileOpen ? "mobile-open" : ""}`}
        aria-label="Application Navigation"
      >
        <div className="erp-sidebar-header">
          <div className="erp-sidebar-brand">
            <div className="erp-brand-logo">U</div>
            {!collapsed && (
              <div>
                <span style={{ letterSpacing: "-0.01em" }}>Universal ERP</span>
                <span style={{ display: "block", fontSize: 10, color: "#64748b", fontWeight: 500 }}>
                  Enterprise Suite
                </span>
              </div>
            )}
          </div>
          <button
            type="button"
            className="erp-sidebar-toggle-btn"
            onClick={onToggleCollapse}
            aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
            title={collapsed ? "Expand sidebar" : "Collapse sidebar"}
          >
            {collapsed ? "➔" : "←"}
          </button>
        </div>

        <nav className="erp-sidebar-nav">
          {groupsOrder.map((grpKey) => {
            const grpItems = groupedItems[grpKey];
            if (!grpItems || grpItems.length === 0) return null;

            return (
              <div key={grpKey} style={{ marginBottom: 6 }}>
                {!collapsed && (
                  <div className="erp-sidebar-section-title">
                    {GROUP_LABELS[grpKey] || grpKey}
                  </div>
                )}
                {grpItems.map((item) => {
                  const isActive = activeId === item.id;
                  return (
                    <button
                      key={item.id}
                      type="button"
                      className={`erp-sidebar-item ${isActive ? "active" : ""}`}
                      onClick={() => {
                        onSelect(item.id);
                        onCloseMobile();
                      }}
                      title={collapsed ? item.label : undefined}
                    >
                      <span className="erp-sidebar-item-icon">{item.icon}</span>
                      {!collapsed && (
                        <>
                          <span className="erp-sidebar-item-label">{item.label}</span>
                          {item.badge !== undefined && (
                            <span className="erp-sidebar-item-badge">{item.badge}</span>
                          )}
                        </>
                      )}
                    </button>
                  );
                })}
              </div>
            );
          })}
        </nav>

        <div className="erp-sidebar-footer">
          <div className="erp-sidebar-user-avatar">
            {userName.charAt(0).toUpperCase()}
          </div>
          {!collapsed && (
            <>
              <div className="erp-sidebar-user-info">
                <div className="erp-sidebar-user-name">{userName}</div>
                <div className="erp-sidebar-user-sub">{userEmail}</div>
              </div>
              {onSignOut && (
                <button
                  type="button"
                  onClick={onSignOut}
                  className="erp-sidebar-toggle-btn"
                  title="Sign out"
                  style={{ color: "#ef4444" }}
                >
                  ⏻
                </button>
              )}
            </>
          )}
        </div>
      </aside>
    </>
  );
}
