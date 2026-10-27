# Architecture

Status: backend host and the **Identity** module (device registration,
TOTP and passkey sign-in, OIDC, user administration, profile); frontend: BFF
proxy, Auth.js, sign-in, the app shell with dashboard and profile. Sync is not built. Use cases:
[use-cases.md](use-cases.md). Sections marked **open** are not decided.

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
| **Identity** | users, devices (key, TOTP secret), passkeys, OAuth clients and tokens (OpenIddict), the Data Protection key ring | `identity` |
| **Sync** | stores (store id, seed id), store versions (header fields + ciphertext), the sync decision; a device is the `device_id` claim | `sync` |

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
| Users, passkeys, lockout | `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10 (no passwords) |
| Authorization server (OIDC) | `OpenIddict.AspNetCore` + `OpenIddict.EntityFrameworkCore` 7.7 |
| Token validation in modules | `Microsoft.AspNetCore.Authentication.JwtBearer` 10 (not OpenIddict `UseLocalServer()`: keeps Identity extractable); Identity hands it the signing key in-process |
| Key ring | `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` 10 (in `identity`) |
| Background jobs | `TickerQ` + `TickerQ.EntityFrameworkCore` 10.5, own `ticker` schema, no dashboard |
| Metrics | `OpenTelemetry.Extensions.Hosting` + `OpenTelemetry.Exporter.Prometheus.AspNetCore` (pre-release: exact pin) |
| In-process dispatch | FastEndpoints command bus (no MediatR) |
| Mapping | by hand; Mapperly if mapping grows (not AutoMapper) |
| Logging | built-in `ILogger`; Serilog + OTLP later |
| Time | `TimeProvider` (no `DateTime.UtcNow` in testable code) |
| Outbound HTTP | typed `HttpClient`, attempts bounded by hand; no SDKs |

Tests: `xunit` 2.9.3 + `xunit.runner.visualstudio` 4.0 +
`Microsoft.NET.Test.Sdk` 18.11 (the trio pinned together), `Shouldly`
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
Api/Application/Domain/Infrastructure split). Today: `Host`, `Shared`,
Identity, and the test projects except `Sync.Tests`. `backend/migrations.Dockerfile`
+ `backend/apply-migrations.sh` are the migrations image (§6).

**Image** (`backend/Dockerfile`): `sdk:10.0` restores from the project files
alone (cached layer), publishes with `-p:Version=$VERSION` and
`-p:SourceRevisionId=$COMMIT`; runtime `aspnet:10.0-noble-chiseled` (182 MB:
no shell, no package manager, no ICU, no tzdata; the backend works in UTC),
non-root (`$APP_UID`), port 8080 (the image's `ASPNETCORE_HTTP_PORTS`;
`dotnet run` uses 5080 from `launchSettings.json`),
`ASPNETCORE_ENVIRONMENT=Production`. No shell means no in-container
healthcheck: probes come from outside. Configuration comes from environment
variables (`ConnectionStrings__AuthServer`, ...).

**Local stack** (`docker-compose.yml`, project `authserver`): `postgres:18`
(volume `pgdata` at `/var/lib/postgresql`, `pg_isready` healthcheck),
`backend`, `frontend` (healthcheck through `node`). Host ports 3100 / 5180 /
5433 by default (`FRONTEND_PORT`, `BACKEND_PORT`, `POSTGRES_PORT` in `.env`),
so the stack runs beside other local projects on 3000 / 5080 / 5432.
`AUTH_SECRET` is required in `.env` (`.env.example`).

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

Wired today: everything below except TickerQ (with the first job) and the
command bus (with the first cross-module call). Policies (`IdentityPolicies`
in `AuthServer.Identity.Contracts`): `account` (a user token), `admin`,
`device` (a device token: has `device_id`). JwtBearer keeps the token's claim
names (`MapInboundClaims = false`).

Configured once in `Program.cs`, never by a module: FastEndpoints (module
assemblies listed), Swagger (`ShortSchemaNames`, non-nullable properties
required), `TimeProvider.System`, the command bus, one JwtBearer scheme,
`AddXxxModule()` per module, forwarded headers (behind ingress), TickerQ,
OpenTelemetry metrics, health checks.

| Route | Purpose |
|---|---|
| `/api/**` | FastEndpoints, `RoutePrefix = "api"` |
| `/connect/**`, `/.well-known/**` | OpenIddict protocol endpoints |
| `GET /api/version` | `{"version":"0.5.0-0-gb249c4e","commit":"<sha>"}`, anonymous; minimal API, not a slice (answers when composition fails). `version`: `git describe --tags --match=* --long` (CI and Docker pass `-p:Version`, a local build asks git; 0.0.0 without either); `commit`: from git, or `-p:SourceRevisionId` (Docker `COMMIT`), null without one |
| `/health/live` | process only, no checks |
| `/health/ready`, `/health` | PostgreSQL check |
| `/metrics` | Prometheus scrape |

### 4.6 Device API

A device talks to `https://<server>` (its configured server name) through the
ingress and the BFF, like a browser. Its private key and the server's TOTP
secret live in its SPIFFS.

**Registration** `POST /register` (the firmware's fixed address; the ingress
routes it to the backend, past the BFF), `Content-Type: application/jwt`, the
body a bare compact JWS. The device waits 15 s, follows no redirect, reads no
body: 2xx is registered, any other status is shown as a refusal.

Header `{"alg":"ES256","typ":"JWT"}`; signature ECDSA P-256 / SHA-256 over
ASCII `base64url(header).base64url(payload)`, raw `r‖s` (64 bytes), by the
key in its own `pub` claim (proof of possession). Payload:

| Claim | Value |
|---|---|
| `email` | typed on the device: ASCII, 3–64, one `@`; unconfirmed |
| `serial` | the device id: the chip's eFuse id, 32 hex (stored lower case); unique |
| `mac` | Wi-Fi MAC, `aa:bb:cc:dd:ee:ff` |
| `totp` | the TOTP secret, base32 (20 bytes: 32 characters): RFC 6238, SHA-1, 30 s, 6 digits |
| `pub` | P-256 point `04‖X‖Y` (65 bytes), base64url (87 characters) |
| `iat` | Unix seconds; within ±10 minutes of the server's clock |

The permission is the account's registration window (use-cases.md UC-1 to
UC-5): open window, not blocked, else `403 device-registration.refused` (one
answer for every cause, a bad signature too); malformed or not ES256: `400
device-registration.invalid`; `iat` off: `400 device-registration.stale`; a
serial on another account: `409 device.taken`. Success: `200 { deviceId,
clientId }` and the window closes. The device makes a new key and TOTP secret
before every attempt, so the same serial on the same account replaces key,
MAC and secret; the device id stays. With no users at all the first
registration needs no window and makes an admin. The window is the only gate:
whoever knows the address inside a window can register (a decided
trade-off). The body holds the TOTP secret: never logged; the secret is stored
encrypted.

**Sign-in**: `private_key_jwt` (RFC 7523, as OpenIddict 7 implements
rfc7523bis). `POST /connect/token`, form: `grant_type=client_credentials`,
`client_id=<clientId>`, `client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer`,
`client_assertion=<JWT>`. The JWT: header `{"alg":"ES256","typ":"client-authentication+jwt"}`;
claims `iss` = `sub` = clientId, `aud` = the issuer exactly as discovery
names it (`https://<server>/`), `jti` random, `iat`, `exp` (minutes). The
access token (minutes, no refresh token) has `sub` = owner, `device_id`.
`GET /api/identity/device` answers who the device is. Refused for a deleted
device or a blocked owner. mTLS was rejected: TLS ends at the ingress.

**Open**, for Sync: endpoints (server header of a store, upload with the
base version, download the newest, report the held version), a stale base
refused, `bytea` up to ~130 KB, how many versions to keep (a TickerQ prune
job), the server certificate pinned on the device.

### 4.7 Identity

- Account: user name, e-mail (both unique), role `user`/`admin` (a column,
  no role tables), `registration_open_until`, `blocked_at`. No password.
- Web sign-in, both setting Identity's own cookie that `/connect/authorize`
  turns into a code: `POST /api/identity/sign-in/totp` (e-mail + a code of
  any of the user's devices, ±1 step, a step once per device, 5 failures
  lock for 15 min) and passkeys (`/api/identity/sign-in/passkey/options` then
  `/api/identity/sign-in/passkey`, ASP.NET Core Identity's WebAuthn, relying
  party = `Identity:PublicOrigin`). Every refusal is `401 sign-in.refused`.
- Tokens: authorization code + PKCE for the BFF client (`authserver-web`),
  refresh tokens not rolling and by reference, revocation, end session,
  userinfo (`/connect/userinfo`: Auth.js refuses a provider without one in
  discovery, though it reads the id token).
  Claims `sub`, `name`, `email`, `role`, audience `authserver.api`. Built
  from the account at every authorize and refresh: a promotion reaches the
  next renewal; a demotion, a block or a delete revokes the subject's tokens.
- Keys: the access-token signing key (ES256) is per process (one replica
  until it is persisted); refresh tokens, codes and cookies are Data
  Protection, key ring in `identity.data_protection_keys`. TOTP secrets are
  AES-256-GCM with `Identity:TotpEncryptionKey` (not in the database).
- Changes that decide who the admins are run under one advisory lock; the
  last unblocked admin cannot be demoted, blocked or deleted (`409
  admin.last`).
- Problem Details `type`: `urn:authserver:problem:<code>`.

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
- **The backend never migrates, in no environment.** The migrations image
  (`backend/migrations.Dockerfile`, CI pushes `-migrations`) holds one EF
  bundle per context (framework-dependent, `--target-runtime linux-x64`) and
  the idempotent SQL (`/sql/<schema>.sql`); `apply-migrations.sh` applies them
  in order with `ConnectionStrings__AuthServer`, the backend's own variable,
  so the cluster Job takes the backend's secret as is. It runs as a
  Kubernetes `Job` before the rollout (not an `initContainer`), in compose
  as the `migrations` service the backend waits for
  (`service_completed_successfully`). Running it again is a no-op.
- Local without Docker: `dotnet ef database update` (§ commands in
  CLAUDE.md). PostgreSQL 18 in Docker: host port 5433; the volume path is
  `/var/lib/postgresql`, not `/var/lib/postgresql/data`.
- Migrations are expand-then-contract: the old code must run on the new
  schema.

## 7. Configuration

`appsettings.json` holds every key with an empty or default value;
secrets never go into git (user-secrets or environment variables locally,
a secret store in the cluster).

```json
{
  "ConnectionStrings": { "AuthServer": "" },
  "Jobs": { "HistoryRetention": { "Succeeded": "2.00:00:00", "Unsuccessful": "30.00:00:00" } },
  "Identity": {
    "PublicOrigin": "",            // the browser's origin: issuer, WebAuthn relying party
    "ClientId": "authserver-web",
    "AccessTokenLifetime": "00:10:00",
    "RefreshTokenLifetime": "08:00:00",
    "InviteWindow": "1.00:00:00",  // registration window without a device
    "DeviceWindow": "01:00:00",    // ... with one
    "TotpEncryptionKey": ""        // secret: 32 bytes base64
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
- Web sign-in: e-mail + device TOTP, or a passkey; no passwords. Tokens
  issued by OpenIddict, validated by JwtBearer. Devices: private_key_jwt.
- Secrets at rest: TOTP secrets encrypted with a key outside the database;
  device keys are public keys; refresh tokens by reference.

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
- `/api/identity/sign-in/**` is forwarded with cookies both ways and no
  token: its answer is the authorization server's cookie.
  `/api/identity/me/passkeys/**` with the token **and** cookies: a passkey
  ceremony keeps its state in the backend's cookie between its two calls.
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

- Sign-in: `/sign-in?returnUrl=<app path>` starts the code flow at once
  (server action); while the backend's cookie lives the browser comes back
  signed in. Without it `/connect/authorize` sends the browser to
  `/sign-in?returnUrl=/connect/authorize…` (same-origin paths only), and only
  then the page shows the form: passkey or e-mail + code, posted from the
  browser (the cookie must land there), then back to `/connect/authorize`.
  Sign-out clears Auth.js's session and goes through `/connect/endsession`;
  the refresh token is not revoked yet.
- The app (`src/app/(app)`): sidebar, page, the user's menu (Gravatar,
  Profile, Sign out). Without a session the layout sends the browser to
  `/sign-in?returnUrl=<path>`; an API 401 (token expired) does the same from
  the query cache. `/` goes to `/dashboard` when signed in.
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

- Sync's device API (§4.6).
- Persisting the signing key (more than one replica).
- How many store versions to keep, and whether the UI may restore an old one.
- Audit module.
- Access token refresh in the BFF (§9).
- Hosting: Kubernetes or a single host with Docker Compose.
