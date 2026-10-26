import { handlers } from "@/auth";

// Auth.js: sign-in, the OIDC callback, sign-out and GET /auth/session (basePath, see auth.ts).
export const { GET, POST } = handlers;
