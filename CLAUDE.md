# ESP32AuthenticatorServer

Sync server and web UI for the ESP32Authenticator hardware authenticator
(firmware: `../ESP32Authenticator`). It stores each user's encrypted store
files (`vault.bin`, `fido.bin`) and gives every device of that user the
newest copy. **Zero knowledge**: the server never has the PIN, the seed
phrase or the key; it reads only the plain store header.

Status: backend host and the Identity module (device registration, TOTP and
passkey sign-in, OIDC, user administration, profile); frontend: BFF proxy,
Auth.js, sign-in, the app shell with dashboard and profile. Use cases with done flags:
[use-cases.md](use-cases.md); tests come first.

## Overview

- **backend/**: .NET 10 **modular monolith**: one deployable, modules
  Identity and Sync, one PostgreSQL database with a schema per module;
  vertical feature slices over a horizontal domain model.
- **frontend/**: Next.js + shadcn/ui + Tailwind, Orval + TanStack Query,
  react-hook-form + zod. Its server half is the BFF (Auth.js): tokens are
  issued by the backend only and never reach the browser.
- Devices call the backend's device API directly; the browser goes through
  the frontend.

Purpose, modules, stack, layout, data, config, security, open questions:
[architecture.md](architecture.md). Read it before writing code. Flow,
commits, CI, release, deploy: [working-with-code.md](working-with-code.md).

## Stack

FastEndpoints (+ Swagger), EF Core 10 + Npgsql, PostgreSQL 18, ASP.NET Core
Identity, OpenIddict, JwtBearer, TickerQ, OpenTelemetry (Prometheus),
xunit + Shouldly + NSubstitute + Testcontainers + ArchUnitNET. Versions only
in `backend/Directory.Packages.props`.

## Commands

```powershell
dotnet build backend\AuthServer.slnx                 # warnings are errors
dotnet test backend\AuthServer.slnx                  # integration tests need Docker
dotnet run --project backend\src\AuthServer.Host     # http://localhost:5080
# once: dotnet user-secrets --project backend\src\AuthServer.Host set Identity:TotpEncryptionKey <openssl rand -base64 32>
#       (also ConnectionStrings:AuthServer, Identity:PublicOrigin http://localhost:3000)
dotnet ef database update --project backend\src\Modules\Identity\AuthServer.Identity --startup-project backend\src\AuthServer.Host --context IdentityDbContext
dotnet ef migrations add <Name> --project backend\src\Modules\Identity\AuthServer.Identity --startup-project backend\src\AuthServer.Host --context IdentityDbContext --output-dir Infrastructure/Persistence/Migrations
docker build -f backend\migrations.Dockerfile -t authserver-migrations:dev backend
docker build -t authserver-backend:dev --build-arg VERSION=$(git describe --tags --match=* --long) --build-arg COMMIT=$(git rev-parse HEAD) backend
docker run --rm -p 8080:8080 authserver-backend:dev  # listens on 8080, non-root
```

Probes: `/health/live`, `/health/ready`, `/health`, `/api/version`, `/metrics`.
The product version is git's (`git describe --tags --match=* --long`), never a file;
a release is a tag `x.y.z` (no `v`).

Whole stack in Docker (`.env.example` → `.env`, set `AUTH_SECRET` and
`TOTP_ENCRYPTION_KEY`; the `migrations` service runs before the backend):

```powershell
docker compose up --build -d   # app :3100, backend :5180, PostgreSQL :5433
docker compose down            # -v also drops the database volume
```

Frontend (in `frontend/`; `.env.example` → `.env.local` first):

```powershell
npm ci
npm run dev                    # http://localhost:3000
npm run check                  # typecheck + lint + vitest
npm run e2e                    # Playwright, production build (npm run build first)
npm run api:generate           # Orval, needs the backend's Swagger
docker build -t authserver-frontend:dev frontend
```

Next.js 16 differs from older versions (`proxy.ts`, Cache Components): read
`frontend/node_modules/next/dist/docs/` before writing Next code
(`frontend/AGENTS.md`).

## Rules

- All docs and code comments are in English. Reply in the language of the
  request.
- Docs are as compact as possible.
- Commits: Conventional Commits; append ` +ai` to the subject; no
  `Co-Authored-By` or similar trailers.
- Module boundary: a module references only its own project, other modules'
  `*.Contracts` and `*.Shared`; everything else is `internal`. No
  cross-module queries or transactions.
- A slice owns its DTOs; slices never call slices; the endpoint is thin.
- Expected failures are `Result` / `Error` mapped to Problem Details.
- `TimeProvider`, never `DateTime.UtcNow`, in testable code.
- `TreatWarningsAsErrors` is on: the build is the gate.
- DTOs and Problem Details `type`s are public API; generated client code is
  never edited.
- Never migrate at startup, in any environment: the migrations job does;
  migrations are expand-then-contract.
- No secrets in git, `appsettings.Development.json` included.
- Store ciphertext, PINs, seed phrases and tokens never in logs or error
  responses.
- Never serve a store version below what the device reports (rollback).
- Frontend: the browser never calls the backend directly and never holds a
  token; nothing token-derived goes into the Auth.js session or a client
  component's props. npm versions pinned exactly.
