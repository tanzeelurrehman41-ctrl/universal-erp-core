import { initializeApp, getApps, type FirebaseApp } from "firebase/app";
import {
  getAuth,
  signInWithPopup,
  signInWithEmailAndPassword,
  createUserWithEmailAndPassword,
  signOut,
  onAuthStateChanged,
  GoogleAuthProvider,
  type Auth,
  type User as FirebaseUser,
} from "firebase/auth";

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

export type ErpAuthUser = {
  uid: string;
  email: string | null;
  displayName: string | null;
  issuer: string;
  expired?: boolean;
  profile: {
    sub: string;
    iss: string;
    email?: string | null;
    name?: string | null;
  };
};

const apiKey = import.meta.env.VITE_FIREBASE_API_KEY?.trim() || "AIzaSyCf_ES3hWRKgQwCvIhuL-C-ky6Zd2-CJx4";
const authDomain = import.meta.env.VITE_FIREBASE_AUTH_DOMAIN?.trim() || "universal-erp-staging.firebaseapp.com";
const projectId = import.meta.env.VITE_FIREBASE_PROJECT_ID?.trim() || "universal-erp-staging";
const appId = import.meta.env.VITE_FIREBASE_APP_ID?.trim() || "1:27822673094:web:6dbbf8997e6775069c9bc7";
const storageBucket = import.meta.env.VITE_FIREBASE_STORAGE_BUCKET?.trim() || "universal-erp-staging.firebasestorage.app";
const messagingSenderId = import.meta.env.VITE_FIREBASE_MESSAGING_SENDER_ID?.trim() || "27822673094";

export const oidcConfigured = Boolean(apiKey && projectId);
export const authConfigured = oidcConfigured;

let app: FirebaseApp | null = null;
let auth: Auth | null = null;
const googleProvider = new GoogleAuthProvider();

export function getFirebaseAuth(): Auth | null {
  if (!authConfigured) return null;
  if (!auth) {
    app = getApps().length > 0
      ? getApps()[0]
      : initializeApp({
          apiKey,
          authDomain,
          projectId,
          appId,
          storageBucket,
          messagingSenderId,
        });
    auth = getAuth(app);
  }
  return auth;
}

export function mapFirebaseUser(user: FirebaseUser | null): ErpAuthUser | null {
  if (!user) return null;
  const issuer = `https://securetoken.google.com/${projectId}`;
  return {
    uid: user.uid,
    email: user.email,
    displayName: user.displayName,
    issuer,
    expired: false,
    profile: {
      sub: user.uid,
      iss: issuer,
      email: user.email,
      name: user.displayName,
    },
  };
}

export async function getAccessToken(): Promise<string | null> {
  const firebaseAuth = getFirebaseAuth();
  if (!firebaseAuth || !firebaseAuth.currentUser) return null;
  try {
    return await firebaseAuth.currentUser.getIdToken();
  } catch {
    return null;
  }
}

export async function signInWithGoogle(): Promise<ErpAuthUser> {
  const firebaseAuth = getFirebaseAuth();
  if (!firebaseAuth) throw new Error("Firebase Authentication is not configured.");
  const credential = await signInWithPopup(firebaseAuth, googleProvider);
  const mapped = mapFirebaseUser(credential.user);
  if (!mapped) throw new Error("Could not map Google user profile.");
  return mapped;
}

export async function signInWithEmail(email: string, pass: string): Promise<ErpAuthUser> {
  const firebaseAuth = getFirebaseAuth();
  if (!firebaseAuth) throw new Error("Firebase Authentication is not configured.");
  const credential = await signInWithEmailAndPassword(firebaseAuth, email, pass);
  const mapped = mapFirebaseUser(credential.user);
  if (!mapped) throw new Error("Could not map user profile.");
  return mapped;
}

export async function signUpWithEmail(email: string, pass: string): Promise<ErpAuthUser> {
  const firebaseAuth = getFirebaseAuth();
  if (!firebaseAuth) throw new Error("Firebase Authentication is not configured.");
  const credential = await createUserWithEmailAndPassword(firebaseAuth, email, pass);
  const mapped = mapFirebaseUser(credential.user);
  if (!mapped) throw new Error("Could not map user profile.");
  return mapped;
}

export async function beginSignOut(): Promise<void> {
  const firebaseAuth = getFirebaseAuth();
  if (!firebaseAuth) return;
  await signOut(firebaseAuth);
}

export async function beginSignIn(): Promise<void> {
  await signInWithGoogle();
}

export function subscribeAuthState(callback: (user: ErpAuthUser | null) => void): () => void {
  const firebaseAuth = getFirebaseAuth();
  if (!firebaseAuth) {
    callback(null);
    return () => {};
  }
  return onAuthStateChanged(firebaseAuth, (user) => {
    callback(mapFirebaseUser(user));
  });
}

// Compatibility functions for legacy callers
export async function initializeOidc(): Promise<ErpAuthUser | null> {
  const firebaseAuth = getFirebaseAuth();
  if (!firebaseAuth) return null;
  await firebaseAuth.authStateReady();
  return mapFirebaseUser(firebaseAuth.currentUser);
}

export async function currentOidcUser(): Promise<ErpAuthUser | null> {
  const firebaseAuth = getFirebaseAuth();
  if (!firebaseAuth) return null;
  return mapFirebaseUser(firebaseAuth.currentUser);
}

export type ApiWorkspaceScope = Pick<TenantCompanyContext, "tenantId" | "companyId">;

export async function apiFetch(
  input: RequestInfo | URL,
  init: RequestInit = {},
  workspace?: ApiWorkspaceScope | null,
  workspaceRequired = true,
): Promise<Response> {
  if (!authConfigured) return fetch(input, init);

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

  return fetch(input, {
    ...init,
    headers,
  });
}
