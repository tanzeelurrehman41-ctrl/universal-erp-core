import { UserManager, WebStorageStateStore, type User } from "oidc-client-ts";

export type TenantCompanyContext = {
  tenantId: string;
  tenantCode: string;
  tenantName: string;
  companyId: string;
  companyCode: string;
  companyName: string;
  baseCurrencyCode: string;
  timezone: string;
};

const authority = import.meta.env.VITE_OIDC_AUTHORITY?.trim();
const clientId = import.meta.env.VITE_OIDC_CLIENT_ID?.trim();
const scope = import.meta.env.VITE_OIDC_SCOPE?.trim();

export const oidcConfigured = Boolean(authority && clientId && scope);

let userManager: UserManager | null = null;

function getUserManager(): UserManager | null {
  if (!oidcConfigured) return null;
  if (userManager) return userManager;

  userManager = new UserManager({
    authority,
    client_id: clientId,
    redirect_uri: `${window.location.origin}/`,
    post_logout_redirect_uri: `${window.location.origin}/`,
    response_type: "code",
    scope,
    automaticSilentRenew: false,
    monitorSession: false,
    loadUserInfo: false,
    userStore: new WebStorageStateStore({ store: window.sessionStorage }),
    stateStore: new WebStorageStateStore({ store: window.sessionStorage }),
  });
  return userManager;
}

export async function initializeOidc(): Promise<User | null> {
  const manager = getUserManager();
  if (!manager) return null;

  const callback = new URLSearchParams(window.location.search);
  if ((callback.has("code") || callback.has("error")) && callback.has("state")) {
    try {
      await manager.signinCallback();
    } finally {
      window.history.replaceState({}, document.title, `${window.location.pathname}${window.location.hash}`);
    }
  }

  return (await manager.getUser()) ?? null;
}

export function currentOidcUser(): Promise<User | null> {
  return getUserManager()?.getUser().then((user) => user ?? null) ?? Promise.resolve(null);
}

export async function beginSignIn(): Promise<void> {
  const manager = getUserManager();
  if (!manager) throw new Error("OIDC sign-in is not configured for this build.");
  await manager.signinRedirect();
}

export async function beginSignOut(): Promise<void> {
  const manager = getUserManager();
  if (!manager) return;
  await manager.signoutRedirect();
}

export async function getAccessToken(): Promise<string | null> {
  const user = await currentOidcUser();
  return user && !user.expired ? user.access_token : null;
}

export type ApiWorkspaceScope = Pick<TenantCompanyContext, "tenantId" | "companyId">;

export async function apiFetch(
  input: RequestInfo | URL,
  init: RequestInit = {},
  workspace?: ApiWorkspaceScope | null,
  workspaceRequired = true,
): Promise<Response> {
  if (!oidcConfigured) return fetch(input, init);

  const token = await getAccessToken();
  if (!token) throw new Error("Sign in again to connect to the shared ERP server.");
  if (workspaceRequired && !workspace) {
    throw new Error("Sign in and select a company before syncing this work.");
  }

  const headers = new Headers(init.headers);
  headers.set("Authorization", `Bearer ${token}`);
  if (workspace) {
    headers.set("X-Tenant-Id", workspace.tenantId);
    headers.set("X-Company-Id", workspace.companyId);
  }
  return fetch(input, { ...init, headers });
}
