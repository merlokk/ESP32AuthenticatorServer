# Use cases

`[x]` done (tests green), `[ ]` not yet. Tests come first: a case is ticked when
its tests pass. Mechanics: [architecture.md](architecture.md) §4.6, §4.7.

Tests (`backend/test/AuthServer.IntegrationTests/` unless noted): UC-1, 4, 5, 7
`DeviceRegistrationTests` (+ `Identity.Tests/Domain/DeviceRegistrationTests`);
UC-2, 3 `RegistrationWindowTests`; UC-6 `DeviceSignInTests`; UC-8 `DeviceNameTests`, `DeviceRemovalTests`; UC-9
`TotpSignInTests` (+ `Identity.Tests/Domain/TotpTests`); UC-10, 12, 13
`PasskeyTests`; UC-11 `AuthorizationCodeFlowTests`, `RenewalTests`,
`EphemeralSigningKeyTests`; UC-14 to 19 `UserAdministrationTests`; UC-20, 21
`ProfileTests`; UC-22 `ConfigurationTests`; UC-30 to 35, 39 `SyncTests` (+
`Sync.Tests/Domain`); UC-38 `DeviceStatusTests` (+
`Identity.Tests/Domain/FirmwareVersionTests`); UC-40
`Host.Tests/PingEndpointTests`; UC-41 to 45 `FirmwareTests` (+
`Firmware.Tests/Domain`); UC-46 `StoreDeletionTests`.

## Identity

### Accounts and the registration window

A user has a user name, an e-mail (both unique), a role (`user` or `admin`)
and a **registration window**: `registration_open_until`, a nullable instant.
Without an open window nothing registers. A successful device registration
closes it (null).

- [x] **UC-1 First device makes the first admin.** While there are no
  users, a device registration needs no window: it creates the user (e-mail
  from the device, user name = the e-mail's local part) with role `admin`.
  Two racing first registrations leave exactly one admin.
- [x] **UC-2 Admin invites a user.** Admin creates a user (e-mail, user name,
  role) with no devices; the window opens for `InviteWindow` (default 24 h).
- [x] **UC-3 Open or extend a window.** Admin for any user, a user for
  themselves. Length: `InviteWindow` while the user has no device,
  `DeviceWindow` (default 1 h) once they have one. Opening again extends
  from now. Closing sets it to null. An admin may choose the length instead:
  `?hours=` 1 to 24 (the UI offers 1 h and 24 h).

### Devices

- [x] **UC-4 Device registration.** `POST /register`, a JWS (ES256) the
  device signs with its new key: e-mail, serial (chip id), MAC, its TOTP
  secret for the server, its P-256 public key, `iat` (±10 min). The server checks the signature, an open
  window on the user with that e-mail and that the user is not blocked, then
  stores the device and closes the window. One refusal for every cause (no
  account, no window, expired window, blocked, bad signature).
- [x] **UC-5 Re-registration.** The same serial on the same user replaces
  the stored device (key, MAC, TOTP secret); the device id stays. A serial
  registered to another user is refused.
- [x] **UC-6 Device sign-in.** `private_key_jwt` (RFC 7523): the device
  signs a short JWT with its key and exchanges it at `/connect/token`
  (`client_credentials`) for an access token naming the device and its
  owner. Refused for a removed device or a blocked owner.
- [x] **UC-7 List my devices.** Id, serial, MAC, registered and last
  seen instants. Never the key or the TOTP secret.
- [x] **UC-8 Name and remove a device.** A user names or removes one of
  theirs, an admin any user's. A name is at most 64 characters; empty
  clears it (the serial shows instead). A removed device gets no token any
  more (one it holds lapses within `AccessTokenLifetime`), its stores stay,
  its serial may register again.

### Web sign-in

- [x] **UC-9 E-mail + TOTP.** The code of any of the user's devices, ±1
  step. A code step is accepted once per device (replay). Wrong codes count
  towards lockout. One refusal for every cause. Blocked users are refused.
- [x] **UC-10 Passkey.** WebAuthn assertion with a discoverable credential;
  blocked users are refused.
- [x] **UC-11 OIDC.** Authorization code + PKCE for the BFF client, refresh
  (not rolling), revocation, end session, userinfo. The access token carries
  `sub`, `name`, `email`, `role`; the signing key is per process.

### Passkeys

- [x] **UC-12 Add a passkey** (signed-in user; up to 10).
- [x] **UC-13 List and remove my passkeys.** A removed passkey no longer
  signs in.
- [x] **UC-21 Rename my passkey** (1 to 64 characters).

### Profile

- [x] **UC-20 Change my user name.** Unique; the e-mail stays (admin only,
  UC-15). Tokens show it from the next renewal.

### Administration (admin only)

- [x] **UC-14 List users** with role, blocked, window, device and passkey
  counts, last sign-in.
- [x] **UC-15 Edit a user**: user name, e-mail.
- [x] **UC-16 Change role.** The last unblocked admin cannot lose the role.
- [x] **UC-17 Block / unblock.** A blocked user cannot sign in, renew or
  register; their sessions are revoked. The last unblocked admin cannot be
  blocked.
- [x] **UC-18 Delete a user** with their devices and passkeys; sessions
  revoked. The last unblocked admin cannot be deleted.
- [x] **UC-19 Role or block changes reach tokens**: a demoted, blocked or
  deleted user's refresh token stops working.
- [x] **UC-22 Read the configuration** the server runs with (the `Identity`
  section; durations in seconds, a secret only as set or not). Read-only: it
  comes from appsettings and the environment.

## Sync

A store is (owner, store id, seed id, kind). The counter orders copies; the
modification time is shown, never compared.

- [x] **UC-30 Heads.** A device names up to 16 stores and gets the version,
  time and nonce the server holds of each, null for one it lacks (the nonce
  tells two writes of one counter apart).
- [x] **UC-31 Upload.** A device's copy is kept only if its version is above
  the newest the server holds, whatever the time; otherwise `409
  sync.not-newer` names the head. Two devices racing with one counter: one
  wins. A file that is not a store, a plain one or one too large is refused.
- [x] **UC-32 Download.** Another device of the owner gets the newest copy
  above the version it reports, else 204; never another user's.
- [x] **UC-33 History.** The server keeps the last `Sync:VersionsToKeep`
  (20) copies of a store and drops older ones on upload.
- [x] **UC-34 Seed groups.** The same store id under another seed id is
  another store.
- [x] **UC-35 Stores in the UI.** The owner sees each store's newest header
  (version, time, writer device, cipher, size, PBKDF2 iterations, key id)
  and its kept versions; never a file.
- [ ] UC-36 Restore an old version from the UI. Open.
- [ ] UC-37 A deleted user's stores go too (cross-module). Open.
- [x] **UC-46 Delete from the UI.** The owner deletes a store with all its
  copies, or one copy; the newest gone, the copy below is the head, the last
  gone, the store goes. A device that holds more uploads it again.
- [x] **UC-39 Stores to adopt.** A device lists the owner's stores (one seed
  id, or all), newest sync first, each with its newest copy's header and
  last sync: what a new or restored device chooses from.

## Devices

The wire protocol: [device-protocol.md](device-protocol.md).

- [x] **UC-38 Status.** A device reports its state (firmware required, Wi-Fi,
  BLE, battery and any other field, 4 KB at most); the report is kept, the
  device marked seen, and the answer names the board's active release when
  the device runs another version (UC-42), else null. The owner and the
  admin see firmware and status with the device, and the store versions and
  seed id it reports; the owner sees each set against the server's newest
  copy (synced, behind, ahead, not on server).
- [x] **UC-40 Ping.** Anonymous: the server answers `ok` and its time.

## Firmware

- [x] **UC-41 Upload.** An admin uploads an image for a board; it is kept
  only if it is this firmware's ESP32-S3 app image, whole (checksum, SHA-256),
  within the OTA slot; its version is read from it; a version is on a board
  once. New releases are inactive.
- [x] **UC-42 Activate.** An admin makes a release its board's active one (the
  previous one inactive), deactivates it, or deletes a release. The active
  one is offered to the board's devices on any other version: an older one
  rolls back.
- [x] **UC-43 List.** A device lists the releases (all, or its board's),
  newest upload first, the active one marked.
- [x] **UC-44 OTA download.** A device fetches an image whole or one `Range`
  at a time (206, `Content-Range`; 416 past the end); ETag is the SHA-256.
- [x] **UC-45 GitHub import.** An admin lists the releases of
  `Firmware:GitHubRepository` (their `.bin` files) and imports one for a
  board: the server fetches it, checks GitHub's size and SHA-256, then keeps
  it as an upload (UC-41). No repository: no import.
