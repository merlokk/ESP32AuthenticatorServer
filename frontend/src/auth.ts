import "server-only";
import NextAuth from "next-auth";
import { getToken } from "next-auth/jwt";
import { publicUrl } from "@/lib/backend";

// Auth.js as the BFF's session (architecture.md §9). The backend's OpenIddict issues every token;
// the session cookie is only an encrypted container for them, and nothing here mints a token.
//
// basePath is /auth rather than the default /api/auth: /api/** belongs to the backend, forwarded
// one-for-one, and Auth.js inside it would shadow part of that space.
//
// Not here yet, deliberately: refreshing the access token. Where the refreshed cookie is written
// (the JWT callback cannot persist it from a server component) and how a rotated refresh token
// behaves under concurrent requests are settled against the real Identity module, not guessed.
// Until then an expired access token is reported as session.error and the user signs in again.

export const AUTH_BASE_PATH = "/auth";

export const { handlers, auth, signIn, signOut } = NextAuth({
  basePath: AUTH_BASE_PATH,
  session: { strategy: "jwt" },
  providers: [
    {
      id: "authserver",
      name: "AuthServer",
      type: "oidc",
      issuer: process.env.AUTH_AUTHSERVER_ISSUER,
      clientId: process.env.AUTH_AUTHSERVER_ID,
      // A public client: PKCE, no secret.
      client: { token_endpoint_auth_method: "none" },
      checks: ["pkce", "state", "nonce"],
      authorization: { params: { scope: "openid profile offline_access" } },
    },
  ],
  callbacks: {
    async jwt({ token, account }) {
      if (account) {
        return {
          ...token,
          accessToken: account.access_token,
          refreshToken: account.refresh_token,
          idToken: account.id_token,
          expiresAt: account.expires_at,
        };
      }
      return token;
    },
    async session({ session, token }) {
      // Only what the UI may know. The session reaches the browser (GET /auth/session), so no
      // token and nothing derived from one goes in here.
      if (token.expiresAt && Date.now() / 1000 >= token.expiresAt) {
        session.error = "AccessTokenExpired";
      }
      return session;
    },
  },
});

/**
 * The backend access token from the request's session cookie, or undefined. Server-side only:
 * the BFF attaches it upstream and the browser never sees it.
 */
export async function getAccessToken(request: Request): Promise<string | undefined> {
  // The cookie's name depends on the scheme the browser used (__Secure- prefix over https).
  const secureCookie = publicUrl(request).protocol === "https:";
  const token = await getToken({ req: request, secret: process.env.AUTH_SECRET, secureCookie });
  if (!token?.accessToken) return undefined;
  if (token.expiresAt && Date.now() / 1000 >= token.expiresAt) return undefined;
  return token.accessToken;
}

declare module "next-auth" {
  interface Session {
    error?: "AccessTokenExpired";
  }
}

declare module "next-auth/jwt" {
  interface JWT {
    accessToken?: string;
    refreshToken?: string;
    idToken?: string;
    expiresAt?: number;
  }
}
