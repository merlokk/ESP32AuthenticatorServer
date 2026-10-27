"use server";

import { AUTH_BASE_PATH, signIn, signOut } from "@/auth";
import { isAuthorizeUrl, safeReturnUrl } from "@/lib/return-url";

// Both actions answer with a URL rather than redirecting: the next stop is /connect/**, a route
// handler the browser must reach by a full navigation (window.location), not the router's fetch.

/**
 * Where the browser goes once the authorization server's cookie is set: back into the flow the
 * backend interrupted, or a new authorization-code flow that ends on the return URL.
 */
export async function continueSignIn(returnUrl: string): Promise<string> {
  const target = safeReturnUrl(returnUrl);
  if (isAuthorizeUrl(target)) return target;
  const url = (await signIn("authserver", { redirectTo: target, redirect: false })) as string;
  // Auth.js answers its own error page, rather than throwing, when discovery fails (backend down):
  // the caller shows "try again" instead of sending the browser there.
  if (new URL(url, "http://origin.invalid").pathname.startsWith(`${AUTH_BASE_PATH}/error`)) {
    throw new Error("The sign-in could not start.");
  }
  return url;
}

/** Ends both sessions: Auth.js's here, the authorization server's at its end-session endpoint. */
export async function signOutEverywhere(): Promise<string> {
  await signOut({ redirect: false });
  // The issuer is this app's public origin (.env.example); "/" is the registered logout URI.
  const home = new URL("/", process.env.AUTH_AUTHSERVER_ISSUER);
  return `/connect/endsession?post_logout_redirect_uri=${encodeURIComponent(home.href)}`;
}
