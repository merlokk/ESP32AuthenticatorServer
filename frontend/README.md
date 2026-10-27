# frontend

Next.js web UI; its server half is the BFF. Stack and rules:
[architecture.md](../architecture.md) §9.

```powershell
copy .env.example .env.local   # then set AUTH_SECRET (npx auth secret)
npm ci
npm run dev                    # http://localhost:3000, backend at BACKEND_URL
npm run check                  # typecheck + lint + unit tests
npm run e2e                    # Playwright against the production build
npm run api:generate           # OpenAPI -> src/api/generated (needs the backend's Swagger)
```

| Path                                                           | Contents                                                                     |
| -------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| `src/app/api/[...path]`                                        | `/api/**` → backend, the session's access token attached                     |
| `src/app/connect/[...path]`, `src/app/.well-known/[...path]`   | OpenIddict's protocol routes, proxied as navigation                          |
| `src/app/api/identity/sign-in/[...path]`                       | the sign-in calls → backend, cookies both ways, no token                     |
| `src/app/auth/[...nextauth]`                                   | Auth.js (basePath `/auth`)                                                   |
| `src/app/api/identity/me/passkeys/[[...path]]`                 | passkey calls → backend, token and cookies                                   |
| `src/app/sign-in`, `src/app/sign-in/start`, `src/app/sign-out` | sign-in page; routes that start the flow and sign out                        |
| `src/app/(app)`                                                | the signed-in app: shell, dashboard, devices, profile, admin (users, system) |
| `src/api/generated`                                            | Orval output (committed, never edited)                                       |
| `src/app/health`                                               | the frontend's own probe                                                     |
| `src/auth.ts`                                                  | Auth.js config, `getAccessToken()`                                           |
| `src/lib/backend.ts`                                           | the forwarding proxy (server-only)                                           |
| `src/api/fetch.ts`                                             | the mutator every generated call goes through                                |
| `src/lib/zod-resolver.ts`                                      | react-hook-form ↔ zod                                                        |
| `src/components/ui`                                            | shadcn/ui components (`npx shadcn add <name>`)                               |
