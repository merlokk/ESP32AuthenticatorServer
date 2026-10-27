# Use cases

`[x]` done (tests green), `[ ]` not yet. Tests come first: a case is ticked when
its tests pass. Mechanics: [architecture.md](architecture.md) §4.6, §4.7.

Tests (`backend/test/AuthServer.IntegrationTests/` unless noted): UC-1, 4, 5, 7
`DeviceRegistrationTests` (+ `Identity.Tests/Domain/DeviceRegistrationTests`);
UC-2, 3 `RegistrationWindowTests`; UC-6 `DeviceSignInTests`; UC-9
`TotpSignInTests` (+ `Identity.Tests/Domain/TotpTests`); UC-10, 12, 13
`PasskeyTests`; UC-11 `AuthorizationCodeFlowTests`, `RenewalTests`,
`EphemeralSigningKeyTests`; UC-14 to 19 `UserAdministrationTests`.

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
  from now. Closing sets it to null.

### Devices

- [x] **UC-4 Device registration.** The device sends e-mail, hardware id,
  MAC, its TOTP secret for the server and its ECDSA P-256 public key, signed
  with the matching private key. The server checks the signature, an open
  window on the user with that e-mail and that the user is not blocked, then
  stores the device and closes the window. One refusal for every cause (no
  account, no window, expired window, blocked, bad signature).
- [x] **UC-5 Re-registration.** The same hardware id on the same user
  replaces the stored device (key, MAC, TOTP secret); the device id stays.
  A hardware id registered to another user is refused.
- [x] **UC-6 Device sign-in.** `private_key_jwt` (RFC 7523): the device
  signs a short JWT with its key and exchanges it at `/connect/token`
  (`client_credentials`) for an access token naming the device and its
  owner. Refused for a removed device or a blocked owner.
- [x] **UC-7 List my devices.** Id, hardware id, MAC, registered and last
  seen instants. Never the key or the TOTP secret.
- [ ] UC-8 Edit and remove devices (user and admin). Later.

### Web sign-in

- [x] **UC-9 E-mail + TOTP.** The code of any of the user's devices, ±1
  step. A code step is accepted once per device (replay). Wrong codes count
  towards lockout. One refusal for every cause. Blocked users are refused.
- [x] **UC-10 Passkey.** WebAuthn assertion with a discoverable credential;
  blocked users are refused.
- [x] **UC-11 OIDC.** Authorization code + PKCE for the BFF client, refresh
  (not rolling), revocation, end session. The access token carries `sub`,
  `name`, `email`, `role`; the signing key is per process.

### Passkeys

- [x] **UC-12 Add a passkey** (signed-in user; up to 10).
- [x] **UC-13 List and remove my passkeys.** A removed passkey no longer
  signs in.

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
