# Architecture

Status: the backend host (health, version, metrics) and the frontend
skeleton (BFF proxy, Auth.js) exist; no modules yet, so no sign-in.
Sections marked **open** are not decided.

## 1. Purpose

Sync server for [ESP32Authenticator](../ESP32Authenticator) (the hardware
authenticator firmware). It keeps the user's encrypted store files
(`vault.bin`: passwords / TOTP, `fido.bin`: FIDO2 credentials) and hands the
newest copy to every device of that user. A web UI shows the account, its
devices and the store history.

**Zero knowledge.** A store is AES-256-GCM with a key = HKDF(seed phrase,
PBKDF2(PIN)); the key never leaves a device. The server holds ciphertext only
and cannot open, edit or validate the payload. It reads the plain 80-byte
store header (firmware `store_header.h`) and nothing else:

| Header field | Server use |
|---|---|
| magic `ESAF`, format, kind, cipher | refuse a file that is not a store (format 3, cipher 1) |
| **store id** (8 bytes) | which store this is: copies on synced devices share it |
| **seed id** (4 bytes) | which seed phrase: devices of one store share it |
| **version** (counter) | ordering, rollback refusal |
| **modification time** (UTC, 0 unknown) | the sync rule |
| key id, salt, nonce, payload length | stored as is, never interpreted |

The header is the GCM associated data: the server cannot check it, the device
does on download. A forged header gets a file refused by the device, never
opened.

**Sync rule** (the firmware's `Newer()`, x4-design.md "Store file and sync"):
the later modification time wins, also when both copies changed; equal times:
the higher version; time 0 loses to any known time. The device writes the
result with version = max(both) + 1. The server applies the same rule and
never returns a copy below the version a device reports (rollback).

## 2. Shape

```
ESP32 device ──HTTPS (device API)──┐
                                   ▼
browser ── frontend (React) ── backend (.NET 10, one process) ── PostgreSQL
```

- **backend/**: a **modular monolith**: one deployable, modules inside, one
  PostgreSQL database with a schema per module. Not microservices.
- **frontend/**: React + TypeScript web UI. Libraries: **open** (§9).
- Devices call the backend's device API directly; the browser only through
  the frontend.

## 3. Modules

| Module | Owns | Schema |
|---|---|---|
| **Identity** | users, passwords, passkeys, OAuth clients and tokens (OpenIddict), device credentials | `identity` |
| **Sync** | devices, stores (store id, seed id), store versions (header fields + ciphertext), the sync decision | `sync` |

An **Audit** module (who changed what, from which device) is a candidate, not
decided.

Rules:

1. A module owns its schema; nothing else reads it. No cross-module joins,
   navigation properties or shared `DbContext`.
2. The only public surface of a module is its `*.Contracts` project;
   everything else is `internal`.
3. Cross-module calls are in-process: an interface from Contracts or a
   command over the FastEndpoints command bus. Never HTTP to itself.
4. `*.Shared` (solution level) holds primitives only: `Result`, `Error`,
   paging shapes. No entities, no domain logic.
5. No cross-module transaction; a cross-module side effect goes through an
   outbox row committed with the action and drained by a job.

Enforced by architecture tests (ArchUnitNET), not by review.

## 4. Backend

### 4.1 Stack

| Purpose | Package |
|---|---|
| Runtime | .NET 10 / ASP.NET Core 10 |
| Endpoints, OpenAPI | `FastEndpoints` + `FastEndpoints.Swagger` 8.3 |
| Persistence | EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL` 10, PostgreSQL 18 |
| Migrations | `Microsoft.EntityFrameworkCore.Design` + `dotnet-ef` (bundles) |
| Users, passwords, passkeys | `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10 |
| Authorization server (OIDC) | `OpenIddict.AspNetCore` + `OpenIddict.EntityFrameworkCore` 7.6 |
| Token validation in modules | `Microsoft.AspNetCore.Authentication.JwtBearer` 10 (not OpenIddict `UseLocalServer()`: keeps Identity extractable) |
| WebAuthn beyond sign-in | `Fido2` + `Fido2.Models` 4.0 (only if needed; not `Fido2.AspNet`) |
| Background jobs | `TickerQ` + `TickerQ.EntityFrameworkCore` 10.4, own `ticker` schema, no dashboard |
| Metrics | `OpenTelemetry.Extensions.Hosting` + `OpenTelemetry.Exporter.Prometheus.AspNetCore` (pre-release: exact pin) |
| In-process dispatch | FastEndpoints command bus (no MediatR) |
| Mapping | by hand; Mapperly if mapping grows (not AutoMapper) |
| Logging | built-in `ILogger`; Serilog + OTLP later |
| Time | `TimeProvider` (no `DateTime.UtcNow` in testable code) |
| Outbound HTTP | typed `HttpClient`, attempts bounded by hand; no SDKs |

Tests: `xunit` 2.9.3 + `xunit.runner.visualstudio` 3.1.5 +
`Microsoft.NET.Test.Sdk` 18.0.1 (the trio pinned together), `Shouldly`
(not FluentAssertions: v8 licence), `NSubstitute` (not Moq),
`coverlet.collector`, `Microsoft.AspNetCore.Mvc.Testing` +
`Testcontainers.PostgreSql` (real PostgreSQL, no in-memory provider),
`TngTech.ArchUnitNET.xUnit`.

### 4.2 Build settings

`Directory.Build.props`: `net10.0`, `LangVersion latest`, `Nullable enable`,
`ImplicitUsings enable`, `TreatWarningsAsErrors true`, `IsPackable false`.
`Directory.Packages.props`: central package management with transitive
pinning; no `.csproj` carries a version. `NuGet.config`: `<clear />`,
nuget.org only, package source mapping `*`.

### 4.3 Layout

```
backend/
├── AuthServer.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── NuGet.config
├── Dockerfile                           # the backend image, context backend/
├── src/
│   ├── AuthServer.Host/                 # composition root, Program.cs, the only deployable
│   ├── AuthServer.Shared/               # primitives only
│   └── Modules/
│       ├── Identity/
│       │   ├── AuthServer.Identity/
│       │   │   ├── Features/            # vertical: one folder per use case
│       │   │   ├── Domain/              # horizontal: aggregates, value objects, errors
│       │   │   ├── Infrastructure/      # horizontal: DbContext, configurations, migrations
│       │   │   └── IdentityModule.cs    # AddIdentityModule()
│       │   └── AuthServer.Identity.Contracts/
│       └── Sync/                        # same shape
└── test/
    ├── AuthServer.ArchitectureTests/
    ├── AuthServer.Identity.Tests/
    ├── AuthServer.Sync.Tests/
    ├── AuthServer.Host.Tests/           # the host's own wiring (WebApplicationFactory, no DB)
    └── AuthServer.IntegrationTests/     # Testcontainers PostgreSQL
```

One implementation project per module (folders, not an
Api/Application/Domain/Infrastructure split). Today only `Host`, `Shared` and
`Host.Tests` exist; modules and the other test projects come with the first
feature.

**Image** (`backend/Dockerfile`): `sdk:10.0` restores from the project files
alone (cached layer), publishes with `-p:Version=$VERSION`; runtime
`aspnet:10.0`, non-root (`$APP_UID`), port 8080 (`Urls` overrides
`appsettings.json`), `ASPNETCORE_ENVIRONMENT=Production`. Configuration
comes from environment variables (`ConnectionStrings__AuthServer`, ...).

### 4.4 Slices

A slice is a folder in `Features/`: `Endpoint.cs`, `Contract.cs` (request),
`Response.cs`; `Handler.cs` only when a second caller (a job, another module)
needs the use case.

- A slice owns its DTOs; no DTO shared between slices.
- Slices do not call slices: shared behaviour goes into `Domain/` or a
  horizontal service.
- The endpoint is thin: route, auth policy, status codes.
- Validation: request shape in value-object factories (`TryFrom`, `Create`
  returning `Result`); business rules on the aggregate; uniqueness as a
  pre-check **plus** a database constraint. FastEndpoints validators are
  singletons: never inject a scoped `DbContext` into one.
- Expected failures are `Result` / `Error`, mapped to Problem Details; not
  exceptions.

### 4.5 Host

Wired today: `TimeProvider`, forwarded headers, OpenTelemetry metrics
(ASP.NET Core meters), health checks, the routes below except the
FastEndpoints and OpenIddict ones. FastEndpoints refuses to start with no
endpoint, so it comes with the first module, as do Swagger, JwtBearer,
TickerQ and the PostgreSQL readiness check.

Configured once in `Program.cs`, never by a module: FastEndpoints (module
assemblies listed), Swagger (`ShortSchemaNames`, non-nullable properties
required), `TimeProvider.System`, the command bus, one JwtBearer scheme,
`AddXxxModule()` per module, forwarded headers (behind ingress), TickerQ,
OpenTelemetry metrics, health checks.

| Route | Purpose |
|---|---|
| `/api/**` | FastEndpoints, `RoutePrefix = "api"` |
| `/connect/**`, `/.well-known/**` | OpenIddict protocol endpoints |
| `GET /api/version` | `{"version":"0.1.0","commit":"<sha>"}`, anonymous; minimal API, not a slice (answers when composition fails). `version`: `<Version>` in `Directory.Build.props` or `-p:Version`; `commit`: from git, or `-p:SourceRevisionId` (Docker `COMMIT`), null without one |
| `/health/live` | process only, no checks |
| `/health/ready`, `/health` | PostgreSQL check |
| `/metrics` | Prometheus scrape |

### 4.6 Device API

**Open.** To decide:

- Device authentication: a device token issued when the device is paired on
  the site (QR code on the e-ink screen), or mTLS with a per-device key.
  The PIN and the seed phrase are never sent.
- Endpoints, roughly: get the server's header for a store id; upload a new
  version (header + ciphertext); download the newest; report the version the
  device holds.
- Concurrency: an upload names the version it was based on; a stale base is
  refused and the device syncs first.
- Size: a store is up to ~130 KB (X4 Pro: 125 FIDO2 credentials); kept as
  `bytea` in PostgreSQL. How many old versions to keep: open (a TickerQ
  prune job).
- The device side: HTTPS client on ESP-IDF, the server certificate pinned.

## 5. API contract

FastEndpoints + DTOs → FastEndpoints.Swagger → one OpenAPI document →
generated TypeScript client in the frontend (generator: §9).

- Request/response DTOs are public API: a rename is a breaking change.
- A Problem Details `type` is public API; `title`/`detail` are not.
- What the caller may do comes back in the response (capabilities); the
  client never infers permissions from role names.
- Generated code is never edited.

## 6. Data

```
database
├── schema identity   ← Identity DbContext only
├── schema sync       ← Sync DbContext only
└── schema ticker     ← TickerQ's store, owned by the host
```

- Each `DbContext` sets `HasDefaultSchema` and keeps its own
  `__EFMigrationsHistory` in its schema.
- EF Core and the Npgsql provider upgrade together (same major). No direct
  `Npgsql` reference without a reason.
- `dotnet ef` always names `--context` and `--project`; the startup project
  is the Host.
- Local: PostgreSQL 18 in Docker (mind the PostgreSQL 18 volume path:
  `/var/lib/postgresql`, not `/var/lib/postgresql/data`); migrations at
  startup or `dotnet ef database update` are fine locally.
- Production: never migrate at startup. One EF migration bundle per context
  (framework-dependent, `--target-runtime linux-x64`), one migrations image,
  run as a Kubernetes `Job` before the rollout (not an `initContainer`).
  Migrations are expand-then-contract: the old code must run on the new
  schema.

## 7. Configuration

`appsettings.json` holds every key with an empty or default value;
secrets never go into git (user-secrets or environment variables locally,
a secret store in the cluster).

```json
{
  "Urls": "http://localhost:5080",
  "ConnectionStrings": { "AuthServer": "" },
  "Jobs": { "HistoryRetention": { "Succeeded": "2.00:00:00", "Unsuccessful": "30.00:00:00" } },
  "Identity": {
    "PublicOrigin": "",
    "AccessTokenLifetime": "00:10:00",
    "RefreshTokenLifetime": "08:00:00",
    "TokenProtectionKey": "",
    "ClientId": "authserver-web"
  },
  "Sync": {}
}
```

Each module binds a typed settings record once; no slice reads
`IConfiguration`. Startup checks run as hosted services registered by
factory, so an `efbundle` (which builds the service provider) never runs them.

## 8. Security

- The server never has the PIN, the seed phrase or the store key; nothing it
  holds opens a store.
- Ciphertext is never logged, never returned except to a device of its
  store's owner.
- A 6-digit PIN falls in minutes to anyone with the files **and** the seed
  phrase; the seed never reaches the server, so the files alone are 2^128
  work (firmware storage-encryption.md).
- Rollback: the server never serves a version below what the device reports;
  the device also refuses it (its highest accepted version is in NVS).
- Web sign-in: password + passkeys (ASP.NET Core Identity); tokens issued by
  OpenIddict, validated by JwtBearer.

## 9. Frontend

**Next.js** (App Router) with its server half as the **BFF**: one
deployable. **Tokens are issued by the backend** (OpenIddict) and only by it;
the BFF holds them server-side and never mints tokens of its own. The
browser sees an httpOnly session cookie, never a token.

```
browser (client components) ──► Next.js server (BFF) ──► backend /api/**
          never a token             holds the tokens        validates them (JwtBearer)
```

- `/api/**` is forwarded as one catch-all, the access token attached, paths
  and bodies untouched: the generated client calls relative paths on the
  app's own origin.
- `/connect/**` and `/.well-known/**` (OpenIddict) are proxied as navigation
  (redirects returned, not followed) so the authorization server is the
  app's origin.
- A token, or anything derived from one, never crosses into a client
  component's props.

| Purpose | Package | Version |
|---|---|---|
| Framework | `next`, `react`, `react-dom` | 16.4, 19.3 |
| UI | **shadcn/ui** (components copied into the repo) + `tailwindcss` 4, `lucide-react` icons | 4.3, 1.61 |
| API client | `orval`: OpenAPI → TanStack Query hooks + zod schemas; no base URL, one `fetch` mutator | 8.45 |
| Server state | `@tanstack/react-query`; live data by `refetchInterval` polling | 5.105 |
| Forms, validation | `react-hook-form` + `zod` (generated where they mirror the contract, client-only rules composed on top); the resolver is ours, `src/lib/zod-resolver.ts`: `@hookform/resolvers`' optional peers make `npm install` and `npm ci` disagree | 7.91, 4.6 |
| Passkeys | none: the native WebAuthn JSON API (`PublicKeyCredential.parseCreationOptionsFromJSON` ...); `@simplewebauthn/browser` only as a fallback | |
| Theme | a cookie read server-side (no `next-themes`) | |
| Tests | `vitest` + `@testing-library/react`; `@playwright/test` e2e (virtual authenticator for passkeys) | 4.1, 16.3, 1.64 |
| Quality | TypeScript strict, ESLint, Prettier | 5.9, 9.39 |

Every version is pinned exactly in `frontend/package.json` (`.npmrc`
`save-exact`). Node 24 LTS (the image); vitest stays on 4.1 because 5
refuses Node 25.

**Layout** (`frontend/README.md` maps the files): `src/app/api/[...path]`,
`src/app/connect/[...path]`, `src/app/.well-known/[...path]` are the
forwarding routes over `src/lib/backend.ts` (server-only: hop-by-hop
headers dropped, a browser `Authorization` never forwarded, cookies only on
the OIDC routes, `X-Forwarded-Host/Proto` set); `src/app/health` is the
frontend's own probe; `src/api/fetch.ts` the Orval mutator (Problem Details
→ `ApiError`). `next.config.ts`: `output: "standalone"`, Cache Components
on, so anything reading the session sits behind `<Suspense>`.

**Image** (`frontend/Dockerfile`): `node:24-bookworm-slim`, `npm ci`,
`next build`, runtime is `.next/standalone/server.js` with `static/` and
`public/`, user `node`, port 3000.

**Session: Auth.js** (`next-auth` 5, exact pin: still beta), an OIDC client
of the backend's OpenIddict (authorization code + PKCE). Its JWE session
cookie is an encrypted container of the backend's access and refresh tokens,
not a token of its own. Chosen over a hand-written `openid-client` handler:
sign-in, callback and sign-out come ready. Sign-out ends both sessions:
Auth.js's and the one at the authorization server.

- `basePath: "/auth"`, not `/api/auth`: `/api/**` is the backend's.
- A public client: PKCE, no secret; scopes `openid profile offline_access`.
- The session object (`GET /auth/session`, readable by the browser) carries
  no token; the BFF reads the token with `getAccessToken(request)`.
- **Token refresh: not built yet**, deliberately. Where the refreshed cookie
  is written and how a rotated refresh token behaves under concurrent
  requests are settled against the real Identity module. Until then an
  expired access token shows as `session.error` and the user signs in
  again.

## 10. Open questions

- Device authentication and pairing (§4.6).
- How many store versions to keep, and whether the UI may restore an old one.
- Audit module.
- Access token refresh in the BFF (§9).
- Hosting: Kubernetes or a single host with Docker Compose.
