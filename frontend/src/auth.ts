import "server-only";
import NextAuth from "next-auth";

// Auth.js as the BFF's session (architecture.md §9). The backend's OpenIddict issues every token;
// the session cookie is only an encrypted container for them, and nothing here mints a token.
//
// basePath is /auth rather than the default /api/auth: /api/** belongs to the backend, forwarded
// one-for-one, and Auth.js inside it would shadow part of that space.
//
// Renewal is not in the JWT callback: a server component cannot write the cookie it would change.
// The routes that use the token renew it and rewrite the cookie themselves (src/lib/session.ts).

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
      // token and nothing derived from one goes in here. An expired access token with a refresh
      // token is still a session: the next /api call renews it.
      if (!token.refreshToken && token.expiresAt && Date.now() / 1000 >= token.expiresAt) {
        session.error = "AccessTokenExpired";
      }
      return session;
    },
  },
});

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
