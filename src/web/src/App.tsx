import { useEffect, useState, type FormEvent } from "react";
import DashboardWorkspace from "./DashboardWorkspace";
import InventoryWorkspace from "./InventoryWorkspace";
import WarehouseWorkspace from "./WarehouseWorkspace";
import PurchasingWorkspace from "./PurchasingWorkspace";
import SalesWorkspace from "./SalesWorkspace";
import TenancyWorkspace from "./TenancyWorkspace";
import SaaSPlatformWorkspace from "./SaaSPlatformWorkspace";
import FinanceWorkspace from "./FinanceWorkspace";
import ManufacturingWorkspace from "./ManufacturingWorkspace";
import FoundationWorkspace from "./FoundationWorkspace";
import HRWorkspace from "./HRWorkspace";
import { AnalyticsWorkspace } from "./AnalyticsWorkspace";
import { ConsolidationWorkspace } from "./ConsolidationWorkspace";
import { BranchDto } from "./branchHelpers";
import { AppShell, Card, Button, Badge, type NavItem } from "./components";
import {
  apiFetch,
  apiUrl,
  beginSignOut,
  getAccessToken,
  signInWithGoogle,
  signInWithEmail,
  signUpWithEmail,
  subscribeAuthState,
  oidcConfigured,
  type ErpAuthUser,
  type TenantCompanyContext,
} from "./auth";
import {
  enqueueProductOperation,
  loadProductOperations,
  requestPersistentBrowserStorage,
  syncPendingProductOperations,
  type OfflineProductOperation,
  type ProductCreatePayload,
  type ProductSummary,
  type SyncAuthorization,
} from "./offlineProductQueue";

type ApiStatus = "checking" | "online" | "offline";
type ModuleSummary = {
  id: string;
  name: string;
  status: string;
  description: string;
};

function workspaceKey(workspace: TenantCompanyContext): string {
  return `${workspace.tenantId}:${workspace.companyId}`;
}

function ownerKeyForUser(user: ErpAuthUser | null): string | undefined {
  const subject = user?.profile?.sub;
  if (!user || typeof subject !== "string" || subject.length === 0) return undefined;
  const issuer = user.profile?.iss;
  return `${typeof issuer === "string" ? issuer : ""}::${subject}`;
}

function readSavedWorkspaces(): TenantCompanyContext[] {
  try {
    const parsed: unknown = JSON.parse(sessionStorage.getItem("universal-erp-workspaces") ?? "[]");
    return Array.isArray(parsed) ? parsed as TenantCompanyContext[] : [];
  } catch {
    return [];
  }
}

const modulePreview: ModuleSummary[] = [
  { id: "foundation", name: "Platform foundation", status: "Architecture only", description: "Tenant, company, location, identity, and database foundations." },
  { id: "core", name: "Core application", status: "Starter", description: "API host and web, Windows, and Android application shells." },
  { id: "inventory", name: "Inventory", status: "In progress", description: "Catalog, units, variants, warehouses, bins, and atomic untracked stock receipts; transfers, reservations, and valuation remain." },
  { id: "purchasing", name: "Purchasing", status: "In progress", description: "Suppliers, purchase orders, goods receipts, inspections, and invoice matching." },
  { id: "finance", name: "Finance", status: "In progress", description: "Chart of Accounts, balanced general ledger journals, and real-time trial balance." },
  { id: "security", name: "Security and access", status: "In progress", description: "OIDC API validation and live membership/permission checks; user provisioning and client sign-in remain." },
  { id: "offline", name: "Offline sync", status: "In progress", description: "Encrypted local journals, atomic snapshot cursors, and a paged change feed; device grants and inbox remain." },
  { id: "reporting", name: "Reporting", status: "Architecture only", description: "Approved datasets, dashboards, exports, and scheduled reports." },
  { id: "saas", name: "SaaS platform", status: "In progress", description: "Tenant provisioning, plans, entitlements, subscriptions, and billing." },
];

export default function App() {
  const [apiStatus, setApiStatus] = useState<ApiStatus>("checking");
  const [modules, setModules] = useState<ModuleSummary[]>(modulePreview);
  const [products, setProducts] = useState<ProductSummary[]>([]);
  const [productCode, setProductCode] = useState("");
  const [productName, setProductName] = useState("");
  const [productType, setProductType] = useState("stock");
  const [productDescription, setProductDescription] = useState("");
  const [catalogMessage, setCatalogMessage] = useState("");
  const [isSavingProduct, setIsSavingProduct] = useState(false);
  const [offlineOperations, setOfflineOperations] = useState<OfflineProductOperation[]>([]);
  const [persistentStorageGranted, setPersistentStorageGranted] = useState<boolean | null>(null);
  const [authUser, setAuthUser] = useState<ErpAuthUser | null>(null);
  const [authReady, setAuthReady] = useState(!oidcConfigured);
  const [authMessage, setAuthMessage] = useState("");
  const [showEmailAuth, setShowEmailAuth] = useState(false);
  const [authEmail, setAuthEmail] = useState("");
  const [authPassword, setAuthPassword] = useState("");
  const [isAuthSubmitting, setIsAuthSubmitting] = useState(false);
  const [workspaces, setWorkspaces] = useState<TenantCompanyContext[]>(() =>
    oidcConfigured ? readSavedWorkspaces() : [],
  );
  const [activeWorkspaceKey, setActiveWorkspaceKey] = useState(() => {
    try {
      return sessionStorage.getItem("universal-erp-active-workspace") ?? "";
    } catch {
      return "";
    }
  });
  const activeWorkspace = workspaces.find((workspace) => workspaceKey(workspace) === activeWorkspaceKey) ?? null;
  const queueOwnerKey = ownerKeyForUser(authUser);
  const [activeNavTab, setActiveNavTab] = useState<
    | "overview"
    | "inventory"
    | "warehouse"
    | "purchasing"
    | "sales"
    | "finance"
    | "manufacturing"
    | "foundation"
    | "hr"
    | "analytics"
    | "consolidation"
    | "saas"
    | "all"
  >("overview");
  const [activeBranchId, setActiveBranchId] = useState<string>("");
  const [branches, setBranches] = useState<BranchDto[]>([]);

  useEffect(() => {
    if (!oidcConfigured) return;
    let isMounted = true;

    const unsubscribe = subscribeAuthState(async (user) => {
      if (!isMounted) return;
      setAuthUser(user);
      setAuthReady(true);
      if (!user) {
        setWorkspaces([]);
        setActiveWorkspaceKey("");
        try {
          sessionStorage.removeItem("universal-erp-workspaces");
          sessionStorage.removeItem("universal-erp-active-workspace");
        } catch {
          // ignore
        }
        return;
      }

      try {
        const response = await apiFetch("/api/v1/contexts", {}, undefined, false);
        if (!response.ok) {
          if (response.status === 401) {
            throw new Error("Authentication expired or invalid. Please sign in again.");
          }
          throw new Error(`Workspace lookup returned HTTP ${response.status}.`);
        }
        const available = (await response.json()) as TenantCompanyContext[];
        if (!isMounted) return;
        setWorkspaces(available);
        try {
          sessionStorage.setItem("universal-erp-workspaces", JSON.stringify(available));
          const savedKey = sessionStorage.getItem("universal-erp-active-workspace") ?? "";
          const nextKey = available.some((workspace) => workspaceKey(workspace) === savedKey)
            ? savedKey
            : available[0]
              ? workspaceKey(available[0])
              : "";
          setActiveWorkspaceKey(nextKey);
          sessionStorage.setItem("universal-erp-active-workspace", nextKey);
        } catch {
          // ignore
        }
        setAuthMessage(available.length === 0
          ? "Your sign-in is active, but no ERP company is linked to this account yet."
          : "");
      } catch (error) {
        if (!isMounted) return;
        setAuthReady(true);
        setAuthMessage(error instanceof Error ? error.message : "Sign-in could not be restored.");
      }
    });

    return () => {
      isMounted = false;
      unsubscribe();
    };
  }, []);

  useEffect(() => {
    let isMounted = true;
    void loadProductOperations()
      .then((operations) => {
        if (isMounted) setOfflineOperations(operations);
      })
      .catch(() => {
        if (isMounted) setCatalogMessage("This browser could not open its local sync queue.");
      });
    void requestPersistentBrowserStorage().then((granted) => {
      if (isMounted) setPersistentStorageGranted(granted);
    });
    return () => {
      isMounted = false;
    };
  }, []);

  useEffect(() => {
    let isMounted = true;

    const checkApi = async () => {
      try {
        const response = await fetch(apiUrl("/health"));
        if (!response.ok) {
          throw new Error("API health check failed");
        }
        if (isMounted) setApiStatus("online");
      } catch {
        if (isMounted) setApiStatus("offline");
      }
    };

    void checkApi();
    const intervalId = window.setInterval(checkApi, 10000);

    return () => {
      isMounted = false;
      window.clearInterval(intervalId);
    };
  }, []);

  useEffect(() => {
    if (apiStatus !== "online") return;
    if (oidcConfigured && (!authUser || authUser.expired || !activeWorkspace)) return;

    let isMounted = true;
    let isRefreshing = false;
    const loadProducts = async () => {
      try {
        const response = await apiFetch("/api/v1/products", {}, activeWorkspace);
        if (!response.ok) {
          throw new Error(response.status === 404 ? "development-only" : "load-failed");
        }
        const productData = (await response.json()) as ProductSummary[];
        if (isMounted) {
          setProducts(productData);
          setCatalogMessage((current) => current === "Could not load products. Check the API connection and try again." ? "" : current);
        }
      } catch (error) {
        if (isMounted) {
          setCatalogMessage(
            error instanceof Error && error.message === "development-only"
              ? "The local catalog is available when the API runs in Development mode."
              : "Could not load products. Check the API connection and try again.",
          );
        }
      }
    };

    const synchronize = async () => {
      if (isRefreshing) return;
      isRefreshing = true;
      try {
        let authorization: SyncAuthorization | undefined;
        if (oidcConfigured && authUser && activeWorkspace && queueOwnerKey) {
          const accessToken = await getAccessToken();
          if (!accessToken) return;
          authorization = {
            accessToken,
            tenantId: activeWorkspace.tenantId,
            companyId: activeWorkspace.companyId,
            companyName: activeWorkspace.companyName,
            ownerKey: queueOwnerKey,
          };
        }
        const operations = await syncPendingProductOperations(authorization);
        if (!isMounted) return;
        setOfflineOperations(operations);
        const locallyConfirmed = operations
          .filter((operation) =>
            operation.status === "accepted" && operation.serverProduct &&
            (!oidcConfigured || (
              operation.ownerKey === queueOwnerKey &&
              operation.tenantId === activeWorkspace?.tenantId &&
              operation.companyId === activeWorkspace?.companyId
            )),
          )
          .map((operation) => operation.serverProduct!);
        setProducts((current) => {
          const byId = new Map(current.map((product) => [product.productId, product]));
          for (const product of locallyConfirmed) byId.set(product.productId, product);
          return [...byId.values()];
        });
        await loadProducts();
      } catch {
        if (isMounted) {
          setCatalogMessage("Sync could not finish. Saved actions remain in this browser's queue.");
        }
      } finally {
        isRefreshing = false;
      }
    };

    void synchronize();
    const intervalId = window.setInterval(synchronize, 15000);
    window.addEventListener("online", synchronize);
    return () => {
      isMounted = false;
      window.clearInterval(intervalId);
      window.removeEventListener("online", synchronize);
    };
  }, [apiStatus, authUser, activeWorkspaceKey, queueOwnerKey]);

  useEffect(() => {
    if (oidcConfigured) setProducts([]);
  }, [activeWorkspaceKey]);

  useEffect(() => {
    if (!activeWorkspace) {
      setBranches([]);
      setActiveBranchId("");
      return;
    }
    const loadBranches = async () => {
      try {
        const res = await apiFetch("/api/v1/branches", {}, activeWorkspace);
        if (res.ok) {
          const data: BranchDto[] = await res.json();
          setBranches(data);
          if (data.length > 0 && !activeBranchId) {
            setActiveBranchId(data[0].id);
          }
        }
      } catch {
        // Fallback
      }
    };
    loadBranches();
  }, [activeWorkspaceKey]);

  const createProduct = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (oidcConfigured && (!authUser || !queueOwnerKey || !activeWorkspace)) {
      setCatalogMessage("Sign in and select a company before saving. Offline entries are tied to that company.");
      return;
    }
    setIsSavingProduct(true);
    setCatalogMessage("");

    try {
      const payload: ProductCreatePayload = {
        productCode,
        name: productName,
        productType,
        description: productDescription,
      };
      const queueScope = oidcConfigured && activeWorkspace && queueOwnerKey
        ? {
            tenantId: activeWorkspace.tenantId,
            companyId: activeWorkspace.companyId,
            companyName: activeWorkspace.companyName,
            ownerKey: queueOwnerKey,
          }
        : undefined;
      const queued = await enqueueProductOperation(payload, queueScope);
      setOfflineOperations(await loadProductOperations());
      setProductCode("");
      setProductName("");
      setProductDescription("");
      setCatalogMessage(`Saved on this device (item ${queued.deviceSequence}); waiting for server confirmation.`);

      let authorization: SyncAuthorization | undefined;
      if (oidcConfigured && activeWorkspace && queueOwnerKey) {
        const accessToken = await getAccessToken();
        if (accessToken) {
          authorization = {
            accessToken,
            tenantId: activeWorkspace.tenantId,
            companyId: activeWorkspace.companyId,
            companyName: activeWorkspace.companyName,
            ownerKey: queueOwnerKey,
          };
        }
      }
      const canSyncNow = apiStatus === "online" &&
        (!oidcConfigured || Boolean(authorization));
      if (canSyncNow) {
        const operations = await syncPendingProductOperations(authorization);
        setOfflineOperations(operations);
        const savedOperation = operations.find((operation) => operation.operationId === queued.operationId);
        if (savedOperation?.serverProduct) {
          setProducts((current) => [
            savedOperation.serverProduct!,
            ...current.filter((product) => product.productId !== savedOperation.serverProduct!.productId),
          ]);
        }
        if (savedOperation?.status === "accepted") {
          setCatalogMessage("Product saved on the server and confirmed on this device.");
        } else if (savedOperation?.status === "conflict") {
          setCatalogMessage("The server found a conflict. Your saved entry remains here for review.");
        } else if (savedOperation?.status === "rejected") {
          setCatalogMessage("The server needs this entry corrected. Its saved details remain here.");
        }
      } else if (oidcConfigured) {
        setCatalogMessage(`Saved on this device for ${activeWorkspace?.companyName ?? "your selected company"}. Sign in and reconnect to sync.`);
      }
    } catch {
      setCatalogMessage("Could not save this entry in browser storage. It was not submitted.");
    } finally {
      setIsSavingProduct(false);
    }
  };

  const editOperationCopy = (operation: OfflineProductOperation) => {
    setProductCode(operation.payload.productCode);
    setProductName(operation.payload.name);
    setProductType(operation.payload.productType);
    setProductDescription(operation.payload.description);
    setCatalogMessage(
      `A copy of saved item ${operation.deviceSequence} is ready to edit. The original stays in the sync queue; saving creates a new operation.`,
    );
    document.getElementById("product-code")?.scrollIntoView({ behavior: "smooth", block: "center" });
    window.setTimeout(() => document.getElementById("product-code")?.focus(), 0);
  };

  useEffect(() => {
    let isMounted = true;

    const loadModules = async () => {
      try {
        const response = await fetch(apiUrl("/api/modules"));
        if (!response.ok) {
          throw new Error("Module catalog request failed");
        }
        const moduleData = (await response.json()) as ModuleSummary[];
        if (isMounted) setModules(moduleData);
      } catch {
        if (isMounted) setModules(modulePreview);
      }
    };

    void loadModules();
    const intervalId = window.setInterval(loadModules, 30000);

    return () => {
      isMounted = false;
      window.clearInterval(intervalId);
    };
  }, []);

  const apiStatusLabel = {
    checking: "Checking API…",
    online: "API online",
    offline: "API offline",
  }[apiStatus];
  const handleGoogleSignIn = async () => {
    try {
      setIsAuthSubmitting(true);
      setAuthMessage("");
      await signInWithGoogle();
    } catch (error) {
      setAuthMessage(error instanceof Error ? error.message : "Google sign-in could not start.");
    } finally {
      setIsAuthSubmitting(false);
    }
  };
  const handleEmailSignIn = async (e: FormEvent) => {
    e.preventDefault();
    if (!authEmail || !authPassword) {
      setAuthMessage("Enter an email and password to sign in.");
      return;
    }
    try {
      setIsAuthSubmitting(true);
      setAuthMessage("");
      await signInWithEmail(authEmail, authPassword);
      setShowEmailAuth(false);
    } catch (error) {
      setAuthMessage(error instanceof Error ? error.message : "Email sign-in failed.");
    } finally {
      setIsAuthSubmitting(false);
    }
  };
  const handleEmailSignUp = async () => {
    if (!authEmail || !authPassword) {
      setAuthMessage("Enter an email and password to create an account.");
      return;
    }
    try {
      setIsAuthSubmitting(true);
      setAuthMessage("");
      await signUpWithEmail(authEmail, authPassword);
      setShowEmailAuth(false);
    } catch (error) {
      setAuthMessage(error instanceof Error ? error.message : "Account creation failed.");
    } finally {
      setIsAuthSubmitting(false);
    }
  };
  const handleSignOut = () => {
    void beginSignOut().catch((error: unknown) => {
      setAuthMessage(error instanceof Error ? error.message : "Sign-out could not finish.");
    });
  };
  const changeWorkspace = (nextKey: string) => {
    setActiveWorkspaceKey(nextKey);
    try {
      sessionStorage.setItem("universal-erp-active-workspace", nextKey);
    } catch {
      setAuthMessage("This browser could not remember the selected company. Choose it again after reopening.");
    }
  };
  const visibleOfflineOperations = oidcConfigured
    ? offlineOperations.filter((operation) => queueOwnerKey && operation.ownerKey === queueOwnerKey)
    : offlineOperations;
  const unresolvedOperations = visibleOfflineOperations.filter((operation) => operation.status !== "accepted");
  const visibleProducts = new Map(products.map((product) => [product.productId, product]));
  for (const operation of visibleOfflineOperations) {
    if (operation.status === "accepted" && operation.serverProduct) {
      if (oidcConfigured && (
        operation.tenantId !== activeWorkspace?.tenantId ||
        operation.companyId !== activeWorkspace?.companyId
      )) continue;
      visibleProducts.set(operation.serverProduct.productId, operation.serverProduct);
    }
  }
  const inventoryEnabled = apiStatus === "online" && (!oidcConfigured || Boolean(authUser && !authUser.expired && activeWorkspace));
  const inventoryStorageScope = `${queueOwnerKey ?? "development"}:${activeWorkspace ? workspaceKey(activeWorkspace) : "local"}`;

  const navItems: NavItem[] = [
    { id: "overview", label: "Executive Overview", icon: "📊", group: "overview" },
    { id: "sales", label: "Sales & POS Checkout", icon: "💳", group: "operations" },
    { id: "inventory", label: "Catalog & Stock", icon: "📦", group: "operations" },
    { id: "warehouse", label: "Warehouse & Barcode", icon: "🏭", group: "operations" },
    { id: "purchasing", label: "Purchasing & GRN", icon: "🛒", group: "operations" },
    { id: "finance", label: "Finance & Ledger", icon: "💰", group: "finance" },
    { id: "analytics", label: "Analytics & BI", icon: "📈", group: "finance" },
    { id: "consolidation", label: "Multi-Branch & Group", icon: "🌐", group: "finance" },
    { id: "manufacturing", label: "Manufacturing & BOM", icon: "⚙️", group: "organization" },
    { id: "hr", label: "HR & Global Payroll", icon: "👥", group: "organization" },
    { id: "foundation", label: "Identity & Roles", icon: "🏢", group: "organization" },
    { id: "saas", label: "SaaS Admin & Tenants", icon: "☁️", group: "platform" },
  ];

  return (
    <AppShell
      navItems={navItems}
      activeNavId={activeNavTab}
      onSelectNav={(id) => setActiveNavTab(id as any)}
      companyName={activeWorkspace?.companyName || (oidcConfigured ? "Select a company" : "Universal ERP Enterprise")}
      branchName={branches.find((b) => b.id === activeBranchId)?.branchName}
      apiStatus={apiStatus}
      userName={authUser?.displayName || authUser?.email || "System Administrator"}
      userEmail={authUser?.email || "admin@universal-erp.io"}
      onSignOut={authUser ? handleSignOut : undefined}
    >
      {/* Unauthenticated Login Card */}
      {oidcConfigured && !authUser && (
        <Card
          title="Sign in to Universal ERP"
          subtitle="Enterprise Multi-Company Authentication (OIDC & Firebase)"
          icon="🔐"
          className="mb-6"
        >
          <div style={{ display: "flex", gap: "12px", alignItems: "center", flexWrap: "wrap", marginBottom: "16px" }}>
            <Button
              variant="primary"
              disabled={!authReady || isAuthSubmitting}
              onClick={handleGoogleSignIn}
            >
              Sign in with Google
            </Button>
            <Button
              variant="secondary"
              disabled={!authReady || isAuthSubmitting}
              onClick={() => setShowEmailAuth(!showEmailAuth)}
            >
              {showEmailAuth ? "Hide Email Form" : "Sign in with Email & Password"}
            </Button>
          </div>

          {showEmailAuth && (
            <form
              onSubmit={handleEmailSignIn}
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr)) auto auto",
                gap: "12px",
                alignItems: "end",
                padding: "16px",
                background: "var(--bg-subtle)",
                borderRadius: "var(--radius-md)",
                border: "1px solid var(--border-base)",
              }}
            >
              <label style={{ display: "flex", flexDirection: "column", gap: 4, fontSize: 13, fontWeight: 600 }}>
                Email address
                <input
                  type="email"
                  placeholder="name@company.com"
                  value={authEmail}
                  onChange={(event) => setAuthEmail(event.target.value)}
                  required
                  style={{
                    height: 38,
                    padding: "0 10px",
                    borderRadius: 6,
                    border: "1px solid var(--border-base)",
                    background: "var(--bg-surface)",
                    color: "var(--text-primary)",
                  }}
                />
              </label>
              <label style={{ display: "flex", flexDirection: "column", gap: 4, fontSize: 13, fontWeight: 600 }}>
                Password
                <input
                  type="password"
                  placeholder="••••••••"
                  value={authPassword}
                  onChange={(event) => setAuthPassword(event.target.value)}
                  required
                  style={{
                    height: 38,
                    padding: "0 10px",
                    borderRadius: 6,
                    border: "1px solid var(--border-base)",
                    background: "var(--bg-surface)",
                    color: "var(--text-primary)",
                  }}
                />
              </label>
              <Button type="submit" variant="primary" disabled={isAuthSubmitting}>
                Sign in
              </Button>
              <Button type="button" variant="outline" disabled={isAuthSubmitting} onClick={handleEmailSignUp}>
                Create account
              </Button>
            </form>
          )}
        </Card>
      )}

      {/* Workspace Context Selector */}
      {oidcConfigured && authUser && (
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: "16px",
            padding: "12px 18px",
            background: "var(--bg-surface)",
            borderRadius: "var(--radius-lg)",
            border: "1px solid var(--border-base)",
            marginBottom: "var(--space-5)",
            boxShadow: "var(--shadow-xs)",
            flexWrap: "wrap",
          }}
        >
          <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
            <span style={{ fontSize: 13, fontWeight: 600, color: "var(--text-secondary)" }}>Company:</span>
            <select
              id="workspace-select"
              value={activeWorkspaceKey}
              onChange={(event) => changeWorkspace(event.target.value)}
              style={{
                height: 34,
                padding: "0 10px",
                border: "1px solid var(--border-base)",
                borderRadius: 6,
                background: "var(--bg-subtle)",
                color: "var(--text-primary)",
                fontWeight: 600,
                fontSize: 13,
              }}
            >
              <option value="">Select a company</option>
              {workspaces.map((workspace) => (
                <option key={workspaceKey(workspace)} value={workspaceKey(workspace)}>
                  {workspace.tenantName} · {workspace.companyName}
                </option>
              ))}
            </select>
          </div>

          {branches.length > 0 && (
            <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
              <span style={{ fontSize: 13, fontWeight: 600, color: "var(--text-secondary)" }}>Branch:</span>
              <select
                id="branch-select"
                value={activeBranchId}
                onChange={(event) => setActiveBranchId(event.target.value)}
                style={{
                  height: 34,
                  padding: "0 10px",
                  border: "1px solid var(--border-base)",
                  borderRadius: 6,
                  background: "var(--bg-subtle)",
                  color: "var(--text-primary)",
                  fontWeight: 600,
                  fontSize: 13,
                }}
              >
                <option value="">All Branches / HQ</option>
                {branches.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.branchCode} · {b.branchName}
                  </option>
                ))}
              </select>
            </div>
          )}

          <div style={{ marginLeft: "auto", fontSize: 12, color: "var(--text-muted)" }}>
            {authUser.expired ? (
              <span style={{ color: "var(--danger-600)", fontWeight: 700 }}>Session expired. Sign in again.</span>
            ) : (
              <span style={{ color: "var(--success-600)" }}>✓ Active Tenant Session</span>
            )}
          </div>
        </div>
      )}

      {oidcConfigured && authMessage && (
        <div
          style={{
            padding: "10px 16px",
            background: "var(--warning-bg)",
            border: "1px solid var(--warning-border)",
            borderRadius: "var(--radius-md)",
            color: "var(--warning-text)",
            fontSize: 13,
            marginBottom: "var(--space-4)",
          }}
          role="status"
        >
          {authMessage}
        </div>
      )}

      {/* Dashboard Executive Overview */}
      {(activeNavTab === "overview" || activeNavTab === "all") && (
        <DashboardWorkspace
          workspace={activeWorkspace}
          onNavigate={(target) => setActiveNavTab(target as any)}
        />
      )}

      {(activeNavTab === "inventory" || activeNavTab === "all") && (
        <>
          <section className="catalog" aria-labelledby="catalog-title">
            <div className="catalog-heading">
              <div>
                <p className="eyebrow">{oidcConfigured ? "INVENTORY · COMPANY WORKSPACE" : "INVENTORY · DEVELOPMENT WORKSPACE"}</p>
                <h2 id="catalog-title">Product catalog</h2>
              </div>
              <span className="tag tag-progress">{activeWorkspace?.companyName ?? (oidcConfigured ? "Select a company" : "Local tenant")}</span>
            </div>
            <p className="catalog-note">
              {oidcConfigured
                ? "Each entry is saved in this browser for the selected company, encrypted locally using AES-GCM, then sent to the shared server when online. Pending work stays here until confirmed."
                : "Each entry is saved in this browser first, encrypted locally using AES-GCM, then synced to the local development tenant. Pending work stays here until confirmed."}
            </p>
            {persistentStorageGranted === false && (
              <p className="catalog-storage-note" role="status">
                This browser did not grant persistent storage permission; pending records remain available in IndexedDB but may be evicted by the browser under device storage pressure. Keep this device available until pending work syncs.
              </p>
            )}
            <form className="catalog-form" onSubmit={createProduct}>
              <label>
                Product code
                <input
                  id="product-code"
                  autoComplete="off"
                  maxLength={64}
                  onChange={(event) => setProductCode(event.target.value)}
                  placeholder="e.g. ITEM-001"
                  required
                  value={productCode}
                />
              </label>
              <label>
                Product name
                <input
                  maxLength={200}
                  onChange={(event) => setProductName(event.target.value)}
                  placeholder="Product name"
                  required
                  value={productName}
                />
              </label>
              <label>
                Type
                <select onChange={(event) => setProductType(event.target.value)} value={productType}>
                  <option value="stock">Stock item</option>
                  <option value="service">Service</option>
                  <option value="non_stock">Non-stock item</option>
                </select>
              </label>
              <label className="description-field">
                Description
                <input
                  maxLength={2000}
                  onChange={(event) => setProductDescription(event.target.value)}
                  placeholder="Optional"
                  value={productDescription}
                />
              </label>
              <button disabled={isSavingProduct || (oidcConfigured && (!authUser || !activeWorkspace))} type="submit">
                {isSavingProduct ? "Saving on device…" : "Save product"}
              </button>
            </form>
            {catalogMessage && <p className="catalog-message" role="status">{catalogMessage}</p>}
            {unresolvedOperations.length > 0 && (
              <section className="offline-queue" aria-label="Saved work waiting to sync">
                <h3>Saved work · {unresolvedOperations.length}</h3>
                {unresolvedOperations.map((operation) => (
                  <article className={`queue-row queue-${operation.status}`} key={operation.operationId}>
                    <div>
                      <strong>{operation.payload.name}</strong>
                      <span className="product-code">{operation.payload.productCode}</span>
                      {oidcConfigured && operation.companyName && <span className="queue-company"> · {operation.companyName}</span>}
                      {operation.message && <p>{operation.message}</p>}
                    </div>
                    <span className="queue-status">
                      {operation.status === "queued" && "Waiting to sync"}
                      {operation.status === "syncing" && "Syncing"}
                      {operation.status === "conflict" && "Needs review"}
                      {operation.status === "rejected" && "Needs correction"}
                    </span>
                    {(operation.status === "conflict" || operation.status === "rejected") && (
                      <button
                        className="queue-edit-button"
                        onClick={() => editOperationCopy(operation)}
                        type="button"
                      >
                        Edit a copy
                      </button>
                    )}
                  </article>
                ))}
              </section>
            )}
            <div className="product-list" aria-live="polite">
              {visibleProducts.size === 0 ? (
                <p className="catalog-empty">No products yet. Add the first item above.</p>
              ) : (
                [...visibleProducts.values()].map((product) => (
                  <article className="product-row" key={product.productId}>
                    <div>
                      <strong>{product.name}</strong>
                      {product.description && <p>{product.description}</p>}
                    </div>
                    <span className="product-code">{product.productCode}</span>
                    <span className="tag">{product.productType.replace("_", " ")}</span>
                  </article>
                ))
              )}
            </div>
          </section>

          <InventoryWorkspace
            enabled={inventoryEnabled}
            canQueue={!oidcConfigured || Boolean(queueOwnerKey && activeWorkspace)}
            persistentStorageGranted={persistentStorageGranted}
            queueScope={oidcConfigured && queueOwnerKey && activeWorkspace ? {
              tenantId: activeWorkspace.tenantId,
              companyId: activeWorkspace.companyId,
              companyName: activeWorkspace.companyName,
              ownerKey: queueOwnerKey,
            } : undefined}
            storageScope={inventoryStorageScope}
            workspace={activeWorkspace}
          />
        </>
      )}

      {(activeNavTab === "warehouse" || activeNavTab === "all") && (
        <WarehouseWorkspace
          enabled={inventoryEnabled}
          workspace={activeWorkspace}
        />
      )}

      {(activeNavTab === "purchasing" || activeNavTab === "all") && (
        <PurchasingWorkspace
          enabled={inventoryEnabled}
          workspace={activeWorkspace}
        />
      )}

      {(activeNavTab === "sales" || activeNavTab === "all") && (
        <SalesWorkspace
          enabled={inventoryEnabled}
          workspace={activeWorkspace}
        />
      )}

      {(activeNavTab === "finance" || activeNavTab === "all") && (
        <FinanceWorkspace
          enabled={inventoryEnabled}
          workspace={activeWorkspace}
        />
      )}

      {(activeNavTab === "manufacturing" || activeNavTab === "all") && (
        <ManufacturingWorkspace
          enabled={inventoryEnabled}
          workspace={activeWorkspace}
        />
      )}

      {(activeNavTab === "foundation" || activeNavTab === "all") && (
        <FoundationWorkspace
          enabled={inventoryEnabled}
          workspace={activeWorkspace}
        />
      )}

      {(activeNavTab === "hr" || activeNavTab === "all") && (
        <HRWorkspace
          enabled={inventoryEnabled}
          workspace={activeWorkspace}
        />
      )}

      {(activeNavTab === "analytics" || activeNavTab === "all") && (
        <AnalyticsWorkspace
          enabled={inventoryEnabled}
          workspace={activeWorkspace}
        />
      )}

      {(activeNavTab === "consolidation" || activeNavTab === "all") && (
        <ConsolidationWorkspace
          tenantId={activeWorkspace?.tenantId ?? "development-tenant"}
          companyId={activeWorkspace?.companyId ?? "development-company"}
          userId={queueOwnerKey ?? "development-user"}
        />
      )}

      {(activeNavTab === "saas" || activeNavTab === "all") && (
        <SaaSPlatformWorkspace
          tenantId={activeWorkspace?.tenantId}
          companyId={activeWorkspace?.companyId}
        />
      )}

      {apiStatus === "offline" && (
        <p className="module-empty">Showing the local package plan; live catalog data loads when the API is online.</p>
      )}

      <section className="grid">
        {modules.map((module) => (
          <article className="module" key={module.id}>
            <span className={`dot ${module.status === "Starter" ? "dot-starter" : module.status === "In progress" ? "dot-progress" : ""}`} />
            <h2>{module.name}</h2>
            <p>{module.description}</p>
            <span className={`tag ${module.status === "Starter" ? "tag-starter" : module.status === "In progress" ? "tag-progress" : ""}`}>
              {module.status}
            </span>
          </article>
        ))}
      </section>

    </AppShell>
  );
}
