# frontend

Next.js web UI; its server half is the BFF. Stack and rules:
[architecture.md](../architecture.md) §9.

```powershell
copy .env.example .env.local   # then set AUTH_SECRET (npx auth secret)
npm ci
npm run dev                    # http://localhost:3000, backend at BACKEND_URL
npm run check                  # typecheck + lint + prettier --check + unit tests
npm run format                 # prettier --write: what the check asks for
npm run e2e                    # Playwright against the production build
npm run api:generate           # OpenAPI -> src/api/generated (needs the backend's Swagger)
```

| Path                                                           | Contents                                                                                       |
| -------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| `src/app/api/[...path]`                                        | `/api/**` → backend, the session's access token attached                                       |
| `src/app/connect/[...path]`, `src/app/.well-known/[...path]`   | OpenIddict's protocol routes, proxied as navigation                                            |
| `src/app/api/device/[...path]`                                 | the device API (device-protocol.md) → backend, the device's own token passed through           |
| `src/app/api/identity/sign-in/[...path]`                       | the sign-in calls → backend, cookies both ways, no token                                       |
| `src/app/auth/[...nextauth]`                                   | Auth.js (basePath `/auth`)                                                                     |
| `src/app/api/identity/me/passkeys/[[...path]]`                 | passkey calls → backend, token and cookies                                                     |
| `src/app/sign-in`, `src/app/sign-in/start`, `src/app/sign-out` | sign-in page; routes that start the flow and sign out                                          |
| `src/app/(app)`                                                | the signed-in app: shell, dashboard, devices, stores, profile, admin (users, firmware, system) |
| `src/api/generated`                                            | Orval output (committed, never edited)                                                         |
| `src/app/health`                                               | the frontend's own probe                                                                       |
| `src/auth.ts`                                                  | Auth.js config                                                                                 |
| `src/lib/backend.ts`                                           | the forwarding proxy (server-only)                                                             |
| `src/lib/session.ts`                                           | the session's access token: renewed, the cookie rewritten (server-only)                        |
| `src/api/fetch.ts`                                             | the mutator every generated call goes through                                                  |
| `src/api/errors.ts`                                            | an `ApiError`'s code and message for the UI                                                    |
| `src/app/providers.tsx`                                        | TanStack Query (an API 401 → `/sign-in/start`) and the theme                                   |
| `src/lib/zod-resolver.ts`                                      | react-hook-form ↔ zod                                                                          |
| `src/lib/webauthn.ts`                                          | the browser's WebAuthn JSON API, no package                                                    |
| `src/lib/return-url.ts`                                        | same-origin `returnUrl` checks                                                                 |
| `src/lib/*.ts` (others)                                        | UI helpers: device status, stores, registration window, durations, Gravatar, version           |
| `src/components/client-only.tsx`                               | render after hydration only (Pitfalls in working-with-code.md)                                 |
| `src/components/ui`                                            | shadcn/ui components (`npx shadcn add <name>`)                                                 |
| `e2e/`, `orval.config.ts`                                      | Playwright tests; the Orval config                                                             |
