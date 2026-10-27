namespace AuthServer.Identity.Contracts;

/// <summary>Authorization policy names; the host registers them, endpoints name them.</summary>
public static class IdentityPolicies
{
    /// <summary>A person's web session: a user token, never a device token.</summary>
    public const string Account = "account";

    /// <summary>An <see cref="Account"/> with the admin role.</summary>
    public const string Admin = "admin";

    /// <summary>A device signed in with its own key.</summary>
    public const string Device = "device";
}

/// <summary>Access-token claims every module may read.</summary>
public static class IdentityClaims
{
    /// <summary>The user id; on a device token, the device's owner.</summary>
    public const string Subject = "sub";

    public const string Name = "name";

    public const string Email = "email";

    public const string Role = "role";

    /// <summary>Present on device tokens only.</summary>
    public const string DeviceId = "device_id";
}

public static class IdentityRoles
{
    public const string Admin = "admin";

    public const string User = "user";
}

/// <summary>The API resource: the audience of every access token.</summary>
public static class IdentityResources
{
    public const string Api = "authserver.api";
}
