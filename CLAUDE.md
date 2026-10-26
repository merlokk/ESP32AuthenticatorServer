# ESP32AuthenticatorServer

Sync server and web UI for the ESP32Authenticator hardware authenticator
(firmware: `../ESP32Authenticator`). It stores each user's encrypted store
files (`vault.bin`, `fido.bin`) and gives every device of that user the
newest copy. **Zero knowledge**: the server never has the PIN, the seed
phrase or the key; it reads only the plain store header.

Status: backend host skeleton (health, version, metrics); no modules yet.

## Overview

- **backend/**: .NET 10 **modular monolith**: one deployable, modules
  Identity and Sync, one PostgreSQL database with a schema per module;
  vertical feature slices over a horizontal domain model.
- **frontend/**: React + TypeScript; libraries not chosen yet.
- Devices call the backend's device API directly; the browser goes through
  the frontend.

Purpose, modules, stack, layout, data, config, security, open questions:
[architecture.md](architecture.md). Read it before writing code.

## Stack

FastEndpoints (+ Swagger), EF Core 10 + Npgsql, PostgreSQL 18, ASP.NET Core
Identity, OpenIddict, JwtBearer, TickerQ, OpenTelemetry (Prometheus),
xunit + Shouldly + NSubstitute + Testcontainers + ArchUnitNET. Versions only
in `backend/Directory.Packages.props`.

## Commands

```powershell
dotnet build backend\AuthServer.slnx                 # warnings are errors
dotnet test backend\AuthServer.slnx
dotnet run --project backend\src\AuthServer.Host     # http://localhost:5080
docker build -t authserver-backend:dev --build-arg VERSION=0.1.0 --build-arg COMMIT=$(git rev-parse HEAD) backend
docker run --rm -p 8080:8080 authserver-backend:dev  # listens on 8080, non-root
```

Probes: `/health/live`, `/health/ready`, `/health`, `/api/version`, `/metrics`.
The product version is `<Version>` in `backend/Directory.Build.props`.

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
- Never migrate at startup outside Development; migrations are
  expand-then-contract.
- No secrets in git, `appsettings.Development.json` included.
- Store ciphertext, PINs, seed phrases and tokens never in logs or error
  responses.
- Never serve a store version below what the device reports (rollback).
