namespace AuthServer.Identity;

/// <summary>The <c>Identity</c> configuration section, bound once (architecture.md §7).</summary>
internal sealed class IdentitySettings
{
    public const string SectionName = "Identity";

    /// <summary>
    /// The origin the browser uses (the BFF's): token issuer, WebAuthn relying party, the base of
    /// every redirect. Never inferred from a request.
    /// </summary>
    public string PublicOrigin { get; set; } = string.Empty;

    /// <summary>The BFF's OpenIddict client (public, PKCE).</summary>
    public string ClientId { get; set; } = "authserver-web";

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Registration window of a user without a device (an invitation).</summary>
    public TimeSpan InviteWindow { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Registration window of a user adding another device.</summary>
    public TimeSpan DeviceWindow { get; set; } = TimeSpan.FromHours(1);

    /// <summary>32 random bytes, base64: AES-GCM key of the stored TOTP secrets.</summary>
    public string TotpEncryptionKey { get; set; } = string.Empty;

    /// <summary>
    /// The newest firmware release (x.y.z) offered to devices that report an older one; empty
    /// offers none.
    /// </summary>
    public string LatestFirmware { get; set; } = string.Empty;

    public string TrimmedPublicOrigin => PublicOrigin.TrimEnd('/');
}
