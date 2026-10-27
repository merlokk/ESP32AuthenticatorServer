using System.Security.Claims;
using AuthServer.Identity.Contracts;

namespace AuthServer.Sync.Infrastructure.Http;

/// <summary>Who calls, from the token Identity issued: the owner (sub) and, on a device token, the device.</summary>
internal static class Callers
{
    public static Guid Owner(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirst(IdentityClaims.Subject)?.Value
            ?? throw new InvalidOperationException("A token without sub passed the policy."));

    public static Guid Device(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirst(IdentityClaims.DeviceId)?.Value
            ?? throw new InvalidOperationException("A device policy passed a token without device_id."));
}
