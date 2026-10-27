// Where the sign-in page sends the browser afterwards. The value arrives in a query string, from
// whoever wrote the link, so only a path on this app's own origin passes.

export const DEFAULT_RETURN_URL = "/";

/** A path on this origin ("/devices?x=1"), or the default for anything else. */
export function safeReturnUrl(value: string | undefined | null): string {
  // "//host" and "/\host" are protocol-relative to a browser; a backslash is never a path here.
  if (!value || !value.startsWith("/") || value.startsWith("//") || value.includes("\\")) {
    return DEFAULT_RETURN_URL;
  }
  const base = "http://origin.invalid";
  let url: URL;
  try {
    url = new URL(value, base);
  } catch {
    return DEFAULT_RETURN_URL;
  }
  return url.origin === base ? `${url.pathname}${url.search}` : DEFAULT_RETURN_URL;
}

/**
 * True for the authorization endpoint: the backend sent the browser here mid-flow, and going back
 * to it resumes that flow (its state and PKCE cookies are already set).
 */
export function isAuthorizeUrl(returnUrl: string): boolean {
  return returnUrl === "/connect/authorize" || returnUrl.startsWith("/connect/authorize?");
}
