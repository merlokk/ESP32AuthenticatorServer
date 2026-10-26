import { forward } from "@/lib/backend";

// /connect/** (OpenIddict's authorize, token, end-session ...) forwarded as navigation: redirects
// returned to the browser, not followed, and the backend's cookies crossing in both directions, so
// the authorization server is this app's origin. No token is attached.
async function handler(request: Request): Promise<Response> {
  return forward(request, { forwardCookies: true, redirect: "manual" });
}

export { handler as GET, handler as POST };
