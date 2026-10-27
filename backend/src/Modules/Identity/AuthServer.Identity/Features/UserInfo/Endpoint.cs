using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using FastEndpoints;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OidcClaims = OpenIddict.Abstractions.OpenIddictConstants.Claims;
using OidcErrors = OpenIddict.Abstractions.OpenIddictConstants.Errors;
using OidcScopes = OpenIddict.Abstractions.OpenIddictConstants.Scopes;

namespace AuthServer.Identity.Features.UserInfo;

/// <summary>
/// OpenIddict's userinfo endpoint (passthrough): the account as it is now, for a user's access
/// token. OpenIddict has validated the token. Auth.js refuses a provider without one in discovery,
/// though it reads the id token instead.
/// </summary>
internal sealed class Endpoint(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Routes("/connect/userinfo");
        Verbs(Http.GET, Http.POST);
        RoutePrefixOverride(string.Empty);
        AllowAnonymous();
        Description(builder => builder.ExcludeFromDescription());
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var principal = (await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal;

        // A device's token names its owner too, but it is not the user signing in.
        var user = principal is null || principal.HasClaim(claim => claim.Type == IdentityClaims.DeviceId)
            ? null
            : await users.FindByIdAsync(principal.GetClaim(OidcClaims.Subject) ?? string.Empty);

        if (principal is null || user is null || !await signIn.CanSignInAsync(user))
        {
            await Send.ResultAsync(Results.Challenge(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = OidcErrors.InvalidToken,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The account no longer signs in.",
                }),
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]));
            return;
        }

        var claims = new Dictionary<string, object>(StringComparer.Ordinal) { [OidcClaims.Subject] = user.Id.ToString() };

        if (principal.HasScope(OidcScopes.Profile))
        {
            claims[OidcClaims.Name] = user.UserName!;
        }

        if (principal.HasScope(OidcScopes.Email))
        {
            claims[OidcClaims.Email] = user.Email!;
        }

        if (principal.HasScope(OidcScopes.Roles))
        {
            claims[OidcClaims.Role] = user.Role;
        }

        await Send.OkAsync(claims, cancellationToken);
    }
}
