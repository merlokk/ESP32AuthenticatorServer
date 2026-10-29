# Device protocol

What the firmware ([ESP32Authenticator](../ESP32Authenticator)) says to the
server, and what it gets back. Use cases: [use-cases.md](use-cases.md) UC-1 to 7
(registration, sign-in), UC-30 to 40 (sync, status, ping), UC-41 to 44
(firmware); how the server does
it: [architecture.md](architecture.md) §4.6.

## Conventions

- The server is `https://<server>`, the name the device is configured with. TLS
  ends at the ingress; every address below is on that origin.
- Bodies are JSON (UTF-8) except where noted. Times are Unix seconds (int64).
  Ids from the store header are lower-case hex: store id 16 characters, seed
  id 8, key id 16, nonce 24.
- A refusal is Problem Details (`application/problem+json`): branch on `type`
  (`urn:authserver:problem:<code>`), never on `title`. Codes are listed with
  each call and in [Errors](#errors).
- A response may come without `Content-Length` (`Transfer-Encoding:
  chunked`): the BFF passes the body through as a stream. Read to the end; the
  size of a piece is in `Content-Range`, of a store in its header.
- `Authorization: Bearer <token>`: the device's access token (Sign-in). Calls
  without it are marked *anonymous*.

| Call | Auth | Purpose |
|---|---|---|
| `GET /api/device/ping` | anonymous | is the server there; its clock |
| `POST /register` | the registration JWS | bind the device to an account |
| `POST /connect/token` | a client assertion | get an access token |
| `GET /api/device/me` | token | who the device is |
| `POST /api/device/status` | token | report the device's state; learn which firmware to run |
| `GET /api/device/stores` | token | the account's stores, to choose one to adopt |
| `POST /api/device/stores/heads` | token | the server's newest version of the device's stores |
| `POST /api/device/stores/files` | token | upload a store |
| `GET /api/device/stores/files/{storeId}/{seedId}/{kind}` | token | download a store |
| `GET /api/device/firmware` | token | the firmware releases |
| `GET /api/device/firmware/{id}/image` | token | a release's image, in pieces (OTA) |

## Ping

`GET /api/device/ping`, anonymous, no body. The device checks it reaches the
server before anything else (and as its connectivity test). Never cached.

```json
200 {"status": "ok", "time": 1793128050}
```

`time` is the server's clock: a device far off can tell. Any other answer, or
none, is "no server".

## Registration

`POST /register` (fixed: the ingress routes it straight to the backend),
`Content-Type: application/jwt`, the body a bare compact JWS. The device waits
15 s, follows no redirect: 2xx is registered, any other status a refusal.

Header `{"alg":"ES256","typ":"JWT"}`; the signature is ECDSA P-256 / SHA-256
over ASCII `base64url(header).base64url(payload)`, raw `r‖s` (64 bytes), by the
key in its own `pub` claim (proof of possession).

| Claim | Value |
|---|---|
| `email` | typed on the device: ASCII, 3–64, one `@` |
| `serial` | the chip's eFuse id, 32 hex; unique |
| `mac` | Wi-Fi MAC, `aa:bb:cc:dd:ee:ff` |
| `totp` | the TOTP secret for web sign-in, base32 (20 bytes, 32 characters): RFC 6238, SHA-1, 30 s, 6 digits |
| `pub` | P-256 point `04‖X‖Y` (65 bytes), base64url (87 characters) |
| `iat` | Unix seconds, within ±10 minutes of the server's clock |

`200 {"deviceId": "<uuid>", "clientId": "<uuid>"}` (the same value; keep it).
The account needs an open registration window (an admin opens it, or the user
on the web: Devices → Allow a new device); the registration closes it. The
first device on an empty server needs none and makes the first admin. A new key
and TOTP secret on every attempt: the same serial on the same account replaces
them, the device id stays.

| Status | `type` | Meaning |
|---|---|---|
| 403 | `device-registration.refused` | no account, no or an expired window, blocked, bad signature: one answer for all |
| 400 | `device-registration.invalid` | malformed, not ES256 |
| 400 | `device-registration.stale` | `iat` off by more than 10 minutes: fix the clock |
| 409 | `device.taken` | the serial is registered to another account |

## Sign-in

`POST /connect/token`, `application/x-www-form-urlencoded`:

```
grant_type=client_credentials
client_id=<clientId>
client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer
client_assertion=<JWT>
```

The assertion: header `{"alg":"ES256","typ":"client-authentication+jwt"}`,
claims `iss` = `sub` = clientId, `aud` = `https://<server>/` (the issuer, with
the slash), `jti` random, `iat`, `exp` (a few minutes), signed with the
device's key. The answer is OAuth's: `{"access_token", "token_type":
"Bearer", "expires_in"}`, no refresh token: sign in again when it expires.
A refusal is an OAuth error (`{"error": "invalid_client" | "invalid_grant",
...}`, 400, 401 or 403): a bad assertion, a removed device, a blocked owner.
A removed device registers again (a new window).

`GET /api/device/me` → `{"deviceId", "userId", "serial"}`.

## Status

`POST /api/device/status`, a JSON object of at most 4 KB. At boot and every so
often (say hourly); it also marks the device seen. `firmware` is required; the
rest is optional, and any other field is kept as sent (shown to the owner on
the web), so new firmware may add fields without a server change.

```json
{
  "firmware": "0.5.0-174-g2f84ffa",
  "board": "x4pro",
  "mac": "aa:bb:cc:dd:ee:ff",
  "wifi": true,
  "ble": false,
  "battery": 87,
  "uptime": 3600,
  "time": 1793128050,
  "passwordsVersion": 57,
  "passkeysVersion": 328,
  "seedId": "4c8be86e"
}
```

| Field | Value |
|---|---|
| `firmware` | `esp_app_desc_t.version`: `x.y.z-<commits>-g<hash>[-dirty]` or `x.y.z-nogit`; 1–32 printable characters |
| `board` | the build target (`devices/config.h` `BoardId`): `x4pro`, `dev`; without it nothing is offered |
| `mac` | Wi-Fi MAC |
| `wifi`, `ble` | on or off |
| `battery` | percent |
| `uptime` | seconds since boot |
| `time` | the device's clock (0: none) |
| `passwordsVersion`, `passkeysVersion` | the version in the header of the device's `vault.bin` / `fido.bin` (as in [Stores](#stores)); left out: no such store |
| `seedId` | the device's seed phrase id (8 hex, the stores' `seedId`); left out: no phrase |

```json
200 {"update": {"id": "01a17f4a-…", "version": "0.6.0-0-gabc1234",
                "size": 1843200, "sha256": "9f2c…"}, "time": 1793128051}
200 {"update": null, "time": 1793128051}
```

`update` is the board's **active** release when `firmware` is any other
version (an admin activating an older release rolls devices back); null when
the device runs it or the board has none. Install it as in
[Firmware](#firmware), with the person's consent. `400 device-status.invalid`:
not an object, over 4 KB, no or a bad `firmware`.

## Stores

### What a store is

A device keeps two stores, each one encrypted file with the plain 80-byte
header of `store_header.h`: `vault.bin` (kind 2, passwords and TOTP) and
`fido.bin` (kind 1, passkeys). The server reads the header only.

- **store id** (8 bytes): random when the store is made, kept by every write
  (a PIN change too). Copies of one store on several devices share it.
- **seed id** (4 bytes): which seed phrase is in the key. Only devices with
  the same seed phrase can open each other's copies, so the server keeps a
  store per (account, store id, **seed id**, kind): the same store id under
  another seed id is another store.
- **version** (uint32): +1 on every write. It alone orders copies. The time
  (`modified`) is shown, never compared: clocks drift.
- **key id** (8 bytes): which key encrypted it (key = HKDF(seed phrase,
  PBKDF2(PIN, salt))). Another key id on another copy means the PIN (or the
  seed) changed on one of the devices: opening it needs the PIN it was
  written under (its salt and iterations are in its header).
- **nonce** (12 bytes): random on every write. One version with another
  nonce is another write.

The server keeps the last 20 copies of each store (`Sync:VersionsToKeep`),
encrypted only (`cipher` 1: a plain store is refused), at most 256 KB a file
(`Sync:MaxFileBytes`). The owner may delete a store or a copy from the UI:
the head then falls (or the store is gone, `version` null), and a device
holding more uploads its copy as usual.

### Choosing a store

A device syncs the stores it has. It adopts one from the server only when it
has none of that kind for its seed phrase: a new device, one restored from its
seed phrase, one wiped.

1. `GET /api/device/stores?seedId=<own seed id>`: the account's stores under
   this seed phrase, newest sync first, each with its newest copy's header and
   when it reached the server (`uploadedAt`, its last sync).
2. For each kind the device lacks:
   - **none**: make a new store (a random store id) and upload it at version 1;
   - **one**: adopt it: download it, keep its store id from now on;
   - **several** (devices that made their own store of a kind before ever
     syncing): show them (kind, version, last sync, the device that wrote it)
     and let the person pick; propose the newest sync. The others stay on the
     server as separate stores; the server cannot merge them (it cannot read
     them).
3. A key id unlike the one the device's PIN gives: ask for the PIN the copy was
   written under, or keep the store but leave it unopened until then.

Without `seedId` the list holds every seed phrase's stores: for showing, not for
adopting.

```json
GET /api/device/stores?seedId=5eed5eed
200 {"stores": [{
  "storeId": "a1b2c3d4e5f60718", "seedId": "5eed5eed", "kind": 2,
  "version": 2, "modified": 1793128050, "cipher": 1, "length": 120,
  "kdfIterations": 200000, "keyId": "1111111111111111",
  "nonce": "0a0b0c0d0e0f101112131415",
  "uploadedByDeviceId": "01a17ec2-98d3-7bbb-a8de-9aa8e0089116",
  "uploadedAt": "2026-10-27T19:07:30.123+00:00"
}]}
```

`400 sync.request-invalid`: `seedId` is not 8 hex.

### Syncing

Every so often (say every 15 minutes, and after a change), for the stores the
device has:

1. `POST /api/device/stores/heads` with its stores (up to 16):

   ```json
   {"stores": [{"storeId": "a1b2c3d4e5f60718", "seedId": "5eed5eed", "kind": 2}]}
   200 {"stores": [{"storeId": "a1b2c3d4e5f60718", "seedId": "5eed5eed", "kind": 2,
                    "version": 2, "modified": 1793128050, "nonce": "0a0b0c0d0e0f101112131415"}]}
   ```

   `version`, `modified`, `nonce` are null for a store the server does not
   hold. `400 sync.request-invalid`: no stores, more than 16, or a bad id or
   kind.
2. Per store, the device compares the server's version and nonce with its own:

   | Server | Device does |
   |---|---|
   | null | upload |
   | version below the device's | upload |
   | version above the device's | download (`?above=` its version) |
   | same version, same nonce | nothing: in sync |
   | same version, another nonce | both wrote since the last sync: download the server's copy and decide (keep it, or keep its own written as server version + 1) |

3. **Upload**: `POST /api/device/stores/files`, `Content-Type:
   application/octet-stream`, the body the store file as is (header, payload,
   tag).

   ```json
   200 {"version": 3, "modified": 1793128090}
   409 {"type": "urn:authserver:problem:sync.not-newer", "version": 3, "modified": 1793128070, ...}
   ```

   Kept only if its version is above the server's newest, whatever the time.
   409 names the server's newest: another device got there first. Download it
   and decide again; to win, write the store with version = server version +
   1. `400 sync.file-invalid` (not a store: magic `ESAF`, format 3, kind,
   length), `400 sync.file-plain` (cipher 0), `413 sync.file-too-large`.
4. **Download**: `GET /api/device/stores/files/{storeId}/{seedId}/{kind}?above=<the device's version>`.

   | Status | Body |
   |---|---|
   | 200 | the store file as uploaded (`application/octet-stream`) |
   | 204 | none: the server has nothing above `above` |
   | 400 | `sync.request-invalid`: a bad id or kind, `above` not a uint32 |
   | 404 | `sync.store-not-found` |

   The server never serves a version at or below `above` (rollback). The
   device still checks what it gets: the GCM tag over header and payload, the
   version against the highest it has accepted (NVS), the store id, seed id and
   kind against what it asked for.

Two devices uploading one version at the same moment: one is kept, the other
gets 409. Nothing is lost on the server: the last 20 copies stay.

## Firmware

Releases are uploaded per board by an admin (web: Admin → Firmware), each
named by its own `esp_app_desc_t.version`; one per board is active.

`GET /api/device/firmware?board=x4pro` (without `board`: every board's),
newest upload first:

```json
200 {"releases": [{
  "id": "01a17f4a-ec9e-7733-85e7-b65f35c133c8", "board": "x4pro",
  "version": "0.6.0-0-gabc1234", "built": "Oct 27 2026 21:07:30",
  "size": 1843200, "sha256": "9f2c…", "active": true,
  "uploadedAt": "2026-10-27T21:30:00+00:00"
}]}
```

`400 firmware.board-unknown`. Any release can be installed, not only the
active one (a person choosing a version on the device).

**OTA**: `GET /api/device/firmware/{id}/image`, one piece per request:

1. `ota::Begin(kWeb, size)`, then from offset 0, `Range: bytes=<offset>-<offset + 64 KB - 1>`:
   `206`, `Content-Range: bytes a-b/size`, the bytes; `ota::Write` them, next
   offset b + 1. A dropped link: ask for the same piece again.
2. After the last piece: the SHA-256 of all bytes must be `sha256` (also the
   `ETag`), then `ota::End`, restart; the trial boot confirms it.

| Status | Meaning |
|---|---|
| 200 | no `Range`: the whole image |
| 206 | the piece |
| 416 | the range starts past the end (`Content-Range: bytes */size`) |
| 404 | `firmware.release-not-found`: deleted meanwhile (or not an id); report status again |

The device still runs its own image check as the bytes stream (chip, project,
segments, checksum): the server checked the same on upload.

## Errors

| Status | `type` | Call |
|---|---|---|
| 400 | `device-registration.invalid`, `device-registration.stale` | register |
| 403 | `device-registration.refused` | register |
| 409 | `device.taken` | register |
| 400, 401, 403 | OAuth `error`: `invalid_client`, `invalid_grant` (not Problem Details) | token |
| 400 | `device-status.invalid` | status |
| 400 | `sync.request-invalid` | stores, heads, download |
| 400 | `sync.file-invalid`, `sync.file-plain` | upload |
| 413 | `sync.file-too-large` | upload |
| 409 | `sync.not-newer` | upload |
| 404 | `sync.store-not-found` | download |
| 400 | `firmware.board-unknown` | firmware list |
| 404 | `firmware.release-not-found` | image |
| 401 | none | a missing or expired token: sign in again |
| 403 | none | a person's token, not a device's |
| 502 | `about:blank` | the server is up but its backend is not: try later |
