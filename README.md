# ESP32AuthenticatorServer

Sync server and web UI for the [ESP32Authenticator](https://github.com/merlokk/ESP32Authenticator)
hardware authenticator. It keeps each user's encrypted store files
(`vault.bin`, `fido.bin`) and gives every device of that user the newest copy.

**Zero knowledge**: the server never has the PIN, the seed phrase or the key.
It reads only the plain store header (store id, seed id, version, modification
time) and never serves a version older than the one a device already has.

Status: early. The backend's Identity module is in place (device
registration, sign-in by device TOTP or passkey, user administration; see
[use-cases.md](use-cases.md)); the sign-in pages and sync are not built yet.

## Parts

| Part        | Stack                                                                                       |
| ----------- | ------------------------------------------------------------------------------------------- |
| `backend/`  | .NET 10 modular monolith (Identity, Sync), FastEndpoints, EF Core, PostgreSQL 18, OpenIddict |
| `frontend/` | Next.js, shadcn/ui + Tailwind, TanStack Query; its server half is the BFF (Auth.js)          |

Devices call the backend's device API directly. The browser talks only to the
frontend; tokens are issued by the backend and never reach the browser.

## Run

Needs Docker. Copy `.env.example` to `.env` and set `AUTH_SECRET`
(`npx auth secret`) and `TOTP_ENCRYPTION_KEY` (`openssl rand -base64 32`), then
(the `migrations` service migrates the database before the backend starts):

```sh
docker compose up --build -d   # app http://localhost:3100, backend :5180, PostgreSQL :5433
docker compose down            # -v also drops the database volume
```

Development without Docker needs the .NET 10 SDK and Node 24+:

```sh
dotnet test backend/AuthServer.slnx
dotnet run --project backend/src/AuthServer.Host   # http://localhost:5080
cd frontend && npm ci && npm run dev               # http://localhost:3000, see frontend/.env.example
```

Backend probes: `/health/live`, `/health/ready`, `/api/version`, `/metrics`.

CI ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) tests both halves and pushes
`ghcr.io/merlokk/esp32authenticatorserver-{backend,frontend,migrations}`: `sha-<short>` on every push,
`latest` on main, `x.y.z` on an `x.y.z` tag (no `v`; it must equal `<Version>`).

## Docs

- [architecture.md](architecture.md): purpose, sync rule, modules, data,
  config, security, open questions.
- [CLAUDE.md](CLAUDE.md): commands and coding rules.
- [frontend/README.md](frontend/README.md): frontend file map.

## License

[MIT](LICENSE)
