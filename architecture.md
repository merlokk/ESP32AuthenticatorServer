# Architecture

Status: backend host, the **Identity** module (device registration, TOTP and
passkey sign-in, OIDC, user administration, profile) and the **Sync** module
(the device sync API, history), the **Firmware** module (releases per board,
OTA); device status and ping; frontend: BFF proxy, Auth.js, sign-in, the app
shell with dashboard, devices, stores, profile and admin (users, firmware,
system).
The firmware has no sync client yet. Use cases:
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
| **version** (counter) | the order: upload accepted only above the head, rollback refusal |
| **modification time** (UTC, 0 unknown) | shown, never compared (device clocks drift) |
| key id, KDF iterations, payload length | shown (a key id change: a PIN or seed change) |
| salt, nonce | stored as is, never interpreted |

The header is the GCM associated data: the server cannot check it, the device
does on download. A forged header gets a file refused by the device, never
opened.

**Sync rule**: the **counter** orders copies, not the time (clocks drift). The
server keeps an upload only if its version is above the newest it holds, and
never serves a copy at or below the version a device reports (rollback). A
device that wants its copy to win writes it with version = max(both) + 1.
The firmware's `Newer()` (x4-design.md "Store file and sync") still puts the
time first: it is to follow this rule.

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
| **Firmware** | releases (board, version, image), the active one per board; `IFirmwareOffers` (Contracts) answers Identity's status call | `firmware` |

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
│       ├── Sync/                        # same shape
│       └── Firmware/                    # same shape, with Contracts
└── test/
    ├── AuthServer.ArchitectureTests/
    ├── AuthServer.Identity.Tests/
    ├── AuthServer.Sync.Tests/
    ├── AuthServer.Firmware.Tests/
    ├── AuthServer.Host.Tests/           # the host's own wiring (WebApplicationFactory, no DB)
    └── AuthServer.IntegrationTests/     # Testcontainers PostgreSQL
```

One implementation project per module (folders, not an
Api/Application/Domain/Infrastructure split). Today: `Host`, `Shared`,
Identity, Sync (no Contracts yet: nothing calls it), Firmware (Contracts:
`IFirmwareOffers`, for Identity), and the test projects.
`backend/migrations.Dockerfile`
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

The wire protocol (every call, field and answer): [device-protocol.md](device-protocol.md).
Here: how the server does it.

- A device talks to `https://<server>` through the ingress and the BFF, like a
  browser; only `/register` is routed past the BFF. Everything else a device
  calls is under `/api/device/`, where the BFF passes the device's own token
  through (§9). Ping is a host minimal API (anonymous, no database).
- Registration (`POST /register`, a JWS signed with the device's new key) and
  sign-in (`private_key_jwt` at `/connect/token`, a token of minutes with
  `sub` = owner and `device_id`) are Identity's. The registration window is the
  only gate (a decided trade-off). The body holds the TOTP secret: never logged;
  stored encrypted. mTLS was rejected: TLS ends at the ingress.
- Status (`POST /api/device/status`) is Identity's: the report is kept as sent
  (`devices.status`, jsonb, 4 KB at most) with its `firmware` read out, the
  device marked seen; the answer is Firmware's (`IFirmwareOffers`): the
  board's active release when the reported version is another (string
  equality, never ordering: activating an older release rolls back).
- Firmware (`/api/firmware/releases` admin, `/api/device/firmware...` device)
  is Firmware's: an upload is checked as the firmware's OTA check does (ESP32-S3
  app image, segments, checksum, SHA-256, `Firmware:Project`) and named by its
  `esp_app_desc_t.version`; unique (board, version); one active per board
  (a partial unique index). An admin can import a release image from
  `Firmware:GitHubRepository` (anonymous GitHub API, checked against its
  size and digest, then as an upload). The image download serves one
  `Range` per request (206; 416 past the end), read with `substring` from an
  uncompressed column.
- Stores (`/api/device/stores...`) are Sync's: a store is (owner, store id,
  seed id, kind); the counter orders copies. An upload is one short
  transaction: upsert the store row, lock it (`for update`), compare the
  counter, insert the version, drop those beyond `Sync:VersionsToKeep`. Two
  devices racing with one counter queue on the row: one wins, the other gets
  409. The head's nonce (random on every write) lets a device see two writes
  of one counter. Plain stores (cipher 0) are refused.
- **Open**: the server certificate pinned on the device.

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
├── schema firmware   ← Firmware DbContext only
└── schema ticker     ← TickerQ's store, owned by the host
```

- Each `DbContext` sets `HasDefaultSchema` and keeps its own
  `__EFMigrationsHistory` in its schema.
- `sync.stores`: a row per store (owner, store id, seed id, kind; unique),
  `head_version` the newest kept. `sync.store_versions`: the header fields,
  the writer `device_id`, upload time and the whole file (`bytea`), unique
  (store, version), the last `Sync:VersionsToKeep` per store.
- `firmware.releases`: board, version, descriptor fields, size, SHA-256,
  `active`, uploader, the image (`bytea`, storage `external`); unique (board,
  version), unique board where active.
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
  "Sync": {
    "VersionsToKeep": 20,          // copies kept per store
    "MaxFileBytes": 262144         // the largest store file accepted
  },
  "Firmware": {
    "Project": "esp32-authenticator", // esp_app_desc_t.project_name; another is refused
    "MaxImageBytes": 8257536,      // the OTA slot (0x7E0000)
    "GitHubRepository": "merlokk/ESP32Authenticator" // owner/name to import release images from; "" none
  }
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
  app's own origin. The body is buffered (a Blob), not streamed: Node's
  fetch re-reads it on a 401 and failed on a stream, so a 401 came back 502.
- `/connect/**` and `/.well-known/**` (OpenIddict) are proxied as navigation
  (redirects returned, not followed) so the authorization server is the
  app's origin.
- `/api/identity/sign-in/**` is forwarded with cookies both ways and no
  token: its answer is the authorization server's cookie.
  `/api/identity/me/passkeys/**` with the token **and** cookies: a passkey
  ceremony keeps its state in the backend's cookie between its two calls.
  `/api/device/**` (the device API) passes the caller's own `Authorization`
  through (a device's token) and attaches no session token, no cookies: the
  only route where a caller's token crosses.
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

- Sign-in: `GET /sign-in/start?returnUrl=<app path>` (a route) starts the
  code flow; while the backend's cookie lives the browser comes back signed
  in. Without it `/connect/authorize` sends the browser to
  `/sign-in?returnUrl=/connect/authorize…` (same-origin paths only), and only
  then the page shows the form: passkey or e-mail + code, posted from the
  browser (the cookie must land there), then back to `/connect/authorize`.
  `POST /sign-out` (a form, same origin only) clears Auth.js's session and
  goes through `/connect/endsession`; the refresh token is not revoked yet.
  No server actions in sign-in or sign-out: an action's id changes with every
  build, so a page loaded before a rollout would call one that is gone.
- The app (`src/app/(app)`): sidebar (Dashboard, Devices, Stores; Admin → Users,
  System for `canAdminister`), page, the user's menu (Gravatar, Profile, Sign out).
  Users: the list (a row opens to its devices); edit the user name, change
  the role, block or unblock,
  delete (role, block and delete not on one's own row), open the registration window for 1 h or 24 h or close it, invite
  by e-mail (an existing address is said so: `user.email-taken`).
  System shows both parts' versions and the running configuration
  (`GET /api/identity/configuration`, admin, read-only, secrets as set or
  not). The frontend's version is git's like the backend's: `VERSION`/
  `COMMIT` build arguments (CI, Docker), else `git describe` in
  `next.config.ts`, fixed at build. Devices: the list and the registration
  window (open, extend, close; polled while open, a registration closes it).
  Stores: each synced store's header (version, device-clock time, writer,
  cipher, size, PBKDF2 iterations, key id) and its kept versions, a key id
  unlike the newest marked (a PIN or seed change).
  Without a session the layout sends the browser to `/sign-in/start`; an API
  401 (renewal refused too) does the same from the query cache. `/` goes to `/dashboard` when signed in.
- `basePath: "/auth"`, not `/api/auth`: `/api/**` is the backend's.
- A public client: PKCE, no secret; scopes `openid profile offline_access`.
- The session object (`GET /auth/session`, readable by the browser) carries
  no token; the BFF reads it from the cookie (`src/lib/session.ts`).
- **Token refresh** in the BFF, not in Auth.js's JWT callback (a server
  component cannot write the cookie). `/api/**` renews the access token with
  the refresh token when it has expired, and once more, the request sent
  again, when the backend answers 401 before then (the signing key is per
  process: a restart refuses every earlier token); the session cookie is
  rewritten on the same response, chunked as Auth.js does. Not rolling, so
  concurrent renewals all succeed. `/sign-in/start` renews instead of
  trusting the session, else a refused token loops between the page and it.
  A session with a refresh token never shows `session.error`; a 401 that
  survives renewal sends the browser to sign in.

## 10. Open questions

- Persisting the signing key (more than one replica).
- Whether the UI may restore an old store version.
- Deleting a user leaves their sync rows: a cross-module side effect (an
  outbox drained by a job, §3 rule 5).
- Audit module.
- Access token refresh in the BFF (§9).
- Hosting: Kubernetes or a single host with Docker Compose.
