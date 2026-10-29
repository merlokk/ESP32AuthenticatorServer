using Microsoft.AspNetCore.Identity;

namespace AuthServer.Identity.Domain;

/// <summary>
/// The account: ASP.NET Core Identity's user (name, e-mail, lockout, security stamp) plus a role,
/// the registration window and the block. No password: sign-in is TOTP or a passkey.
/// </summary>
internal sealed class ApplicationUser : IdentityUser<Guid>
{
    public string Role { get; set; } = UserRoles.User;

    /// <summary>Until when a device may register to this account; null: closed.</summary>
    public DateTimeOffset? RegistrationOpenUntil { get; set; }

    public DateTimeOffset? BlockedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSignInAt { get; set; }

    public bool IsActiveAdmin => Role == UserRoles.Admin && BlockedAt is null;
}

/// <summary>A WebAuthn credential, with the instant it last signed in.</summary>
internal sealed class PasskeyCredential : IdentityUserPasskey<Guid>
{
    public DateTimeOffset? LastUsedAt { get; set; }
}

/// <summary>
/// A hardware authenticator registered to a user. Its id is also its OpenIddict client id; the
/// public key signs its client assertions, the TOTP secret (encrypted) signs its user in.
/// </summary>
internal sealed class Device
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The chip's eFuse id, 32 lower-case hex characters; unique.</summary>
    public string Serial { get; set; } = string.Empty;

    public string Mac { get; set; } = string.Empty;

    /// <summary>What its owner calls it ("Kitchen X4"); null: unnamed, shown by its serial.</summary>
    public string? Name { get; set; }

    /// <summary>Uncompressed SEC1 P-256 point, 65 bytes.</summary>
    public byte[] PublicKey { get; set; } = [];

    /// <summary>The TOTP secret, encrypted with Identity:TotpEncryptionKey.</summary>
    public byte[] TotpSecret { get; set; } = [];

    /// <summary>The last TOTP step accepted from this device; a step is accepted once.</summary>
    public long LastTotpStep { get; set; }

    public DateTimeOffset RegisteredAt { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>The firmware version of the last status report (git describe).</summary>
    public string? Firmware { get; set; }

    /// <summary>The last status report as the device sent it (JSON): Wi-Fi, BLE, battery and the rest.</summary>
    public string? Status { get; set; }
}
