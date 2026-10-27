# Working with code

How a change gets from the working tree to the server. Stack and rules:
[architecture.md](architecture.md), [CLAUDE.md](CLAUDE.md); commands:
[CLAUDE.md](CLAUDE.md#commands).

## 1. Flow: straight to `main`

```powershell
git pull --rebase        # before you start and before you push
git commit               # on main
git push origin main
```

No feature branches, no pull requests. A change is done when it is pushed,
CI is green and, for anything a user sees, it runs on the server (§7). A
branch only for something that cannot land in pieces (a long-running
experiment); rebase it onto `main` and keep it short.

## 2. Before the commit

The diff is the only review a change gets.

- **Tests first.** A use case in [use-cases.md](use-cases.md) gets its tests,
  red for the right reason, then the code; tick `[x]` when they pass.
- **Run what you changed.** Backend: `dotnet build` (warnings are errors) and
  `dotnet test`. Frontend: `npm run check`, plus `npm run build` and
  `npm run e2e` when a page or a route changed. If something could not be run
  (no backend, no device), say so in the commit body.
- **Read the diff**: `git diff --staged`. No secrets, no tokens, no store
  bytes, no machine paths.
- **Look at it in a browser** when the UI changed: `docker compose up --build
  -d` (app :3100), or `dotnet run` + `npm run dev` (:3000).
- **Keep `main` deployable.** Migrations are expand-then-contract; a change
  the old code cannot run beside is two commits.
- **Docs in the same commit**: the status lines in `CLAUDE.md` and
  `architecture.md`, the file map in `frontend/README.md`, `use-cases.md`.

## 3. Commits

Conventional Commits, subject ending in ` +ai` when AI-assisted, no
`Co-Authored-By` trailers. Scope by part or module: `feat(identity): …`,
`feat(frontend): …`, `ci: …`, `docs: …`. The body says why, and what was not
verified. One concern per commit; backend and frontend together only when
neither works alone.

## 4. Language

English in code, comments, docs and commit messages. Docs as short as they
can be.

## 5. CI

[`.github/workflows/ci.yml`](.github/workflows/ci.yml), on every push to `main`,
on `x.y.z` tags and on pull requests:

1. `backend`: build + all tests (integration tests in Testcontainers).
2. `frontend`: `npm ci` + `npm run check`. Not e2e: run it locally.
3. `images`: three images to GHCR, `ghcr.io/merlokk/esp32authenticatorserver-`
   `backend`, `frontend`, `migrations`; tags `sha-<short>` always, `latest`
   on `main`, `x.y.z` and `x.y` on a tag. A pull request builds, never pushes.

Watch it: `gh run watch --exit-status`. A red run is fixed by the next
commit on `main`, not by a revert unless the server is broken.

## 6. Release

The version is `git describe --tags --match=* --long`, never a file. A
release is a tag `x.y.z` (no `v`) on a green `main` commit:

```powershell
git tag 0.6.0
git push origin 0.6.0    # CI builds and pushes the 0.6.0 images
```

## 7. Deploy

Production: <https://auth.merlokk.com>, one k3s node. Deployment lives in a
separate infrastructure repository, not here: the three images run under one
tag (a release `x.y.z` or `sha-<short>`); setting that tag and applying it
rolls the app. The migrations Job runs before the rollout with the backend's
connection string; the backend never migrates. TLS ends at the ingress;
`/register` goes straight to the backend, everything else to the frontend.

After a rollout:

- `GET /api/version` answers the new version and commit (Admin → System
  shows the frontend's too);
- `/health/ready` is 200;
- the changed page or call works in a browser (sign in, then the feature).

Logs and pods: `kubectl`, context `merlokk`, namespace `authserver`. The API
port is closed to the internet: open a tunnel first and keep it running.

```sh
ssh -N -L 6443:127.0.0.1:6443 root@<server>
kubectl --context merlokk -n authserver get pods
kubectl --context merlokk -n authserver logs deploy/frontend --since=1h   # [auth][error] lines
kubectl --context merlokk -n authserver logs deploy/backend --since=1h
```

A tunnel opened in Windows is not `127.0.0.1` inside WSL: run `kubectl` on the
same side as `ssh`.

A failed migration stops the rollout and the old pods keep running. Roll
back by deploying the previous tag; a migration is never rolled back, which
is why every one is expand-then-contract.

## 8. Pitfalls met so far

- **Next.js 16, Cache Components**: anything reading the request (`auth()`,
  `searchParams`, `cookies()`) sits behind `<Suspense>`; call
  `await connection()` before `auth()`, or the prerender trips over the
  random bytes Auth.js draws. Read `frontend/node_modules/next/dist/docs/`
  before writing Next code.
- **A device through the BFF**: devices reach `https://<server>` like a
  browser, and `/api/**` drops a caller's `Authorization`. A device endpoint
  lives under `/api/sync/device/` (its route passes the token through) or is
  routed past the BFF by the ingress (`/register`).
- **The proxy buffers bodies**: Node's fetch re-reads a body on a 401 and
  failed on a stream (a 502 for an expired token). `forward.test.ts` keeps it.
- **BFF cookies**: `/api/**` drops cookies both ways. A backend call whose
  answer or state is a cookie (sign-in, a passkey ceremony) needs its own
  route with `forwardCookies`. Integration tests do not catch it: the test
  client keeps cookies; the walk below does.
- **Auth.js and discovery**: it refuses a provider without
  `userinfo_endpoint` (the callback fails: "Server error"), and answers its
  own error URL instead of throwing when discovery fails.
- **A route handler is not a page**: reach `/connect/**` by
  `window.location`, not the router or a server-action `redirect()`.
- **Hydration inside `<Suspense>`**: a part that streams in later hydrates
  after the sidebar has filled the query cache; render cached data there
  only after hydration (`ClientOnly`), or it will not match the server.
- **Hidden pages stay in the DOM**: Next 16 keeps recent pages mounted but
  hidden (`<Activity>`); a test locates visible elements only (role
  queries, `:visible`), or it finds the page before.
- **Server actions and rollouts**: an action's id changes with every build;
  a tab opened before a rollout calls one that is gone ("Failed to find
  Server Action"). Sign-in and sign-out are routes for that reason.
- **Generated client**: `npm run api:generate` needs the backend's Swagger
  (`BACKEND_URL`); generated code is committed and never edited.
- **Walk the whole path before a release** that touches sign-in: register a
  device as the firmware does (first one = admin on an empty database), sign
  in by TOTP, then passkeys on a Chromium virtual authenticator. Unit and e2e
  tests run without a backend and do not cover it.
