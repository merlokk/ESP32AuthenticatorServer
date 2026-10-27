using System.Collections.Immutable;
using System.Security.Claims;
using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthServer.Identity.Infrastructure.Tokens;

/// <summary>
/// What a token says, built from the account as it is now: at authorize, at every refresh and at
/// every device sign-in. A role change reaches the next renewal.
/// </summary>
internal static class TokenPrincipals
{
    public static ClaimsPrincipal ForUser(ApplicationUser user, ImmutableArray<string> scopes)
    {
        var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id.ToString())
            .SetClaim(Claims.Name, user.UserName)
            .SetClaim(Claims.Email, user.Email)
            .SetClaim(Claims.Role, user.Role);

        identity.SetScopes(scopes);
        identity.SetResources(IdentityResources.Api);

        identity.SetDestinations(claim => claim.Type switch
        {
            Claims.Subject or Claims.Name or Claims.Email or Claims.Role => [Destinations.AccessToken, Destinations.IdentityToken],
            _ => [Destinations.AccessToken],
        });

        return new ClaimsPrincipal(identity);
    }

    public static ClaimsPrincipal ForDevice(Device device)
    {
        var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);

        identity.SetClaim(Claims.Subject, device.UserId.ToString())
            .SetClaim(IdentityClaims.DeviceId, device.Id.ToString());

        identity.SetResources(IdentityResources.Api);
        identity.SetDestinations(_ => [Destinations.AccessToken]);

        return new ClaimsPrincipal(identity);
    }
}
