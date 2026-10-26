import { forward } from "@/lib/backend";

// OIDC discovery and the JWKS, served from the issuer's own origin. GET only, no token, no cookies.
export async function GET(request: Request): Promise<Response> {
  return forward(request, { forwardCookies: false, redirect: "manual" });
}
