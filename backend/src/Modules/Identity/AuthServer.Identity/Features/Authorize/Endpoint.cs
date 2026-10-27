using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Tokens;
using FastEndpoints;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace AuthServer.Identity.Features.Authorize;

/// <summary>
/// OpenIddict's authorization endpoint (passthrough): the identity cookie becomes a code; without
/// one, the browser goes to the frontend's sign-in page and comes back here.
/// </summary>
internal sealed class Endpoint(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IdentitySettings settings) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Routes("/connect/authorize");
        Verbs(Http.GET, Http.POST);
        RoutePrefixOverride(string.Empty);
        AllowAnonymous();
        Description(builder => builder.ExcludeFromDescription());
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("No OpenIddict request: the server middleware did not run.");

        var cookie = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var user = cookie.Succeeded ? await users.GetUserAsync(cookie.Principal!) : null;

        // No cookie, a deleted account or a blocked one: sign in (again).
        if (user is null || !await signIn.CanSignInAsync(user))
        {
            if (cookie.Succeeded)
            {
                await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            }

            var returnUrl = HttpContext.Request.PathBase + HttpContext.Request.Path + HttpContext.Request.QueryString;

            await Send.RedirectAsync(
                $"{settings.TrimmedPublicOrigin}/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}",
                isPermanent: false,
                allowRemoteRedirects: true);
            return;
        }

        await Send.ResultAsync(Results.SignIn(
            TokenPrincipals.ForUser(user, request.GetScopes()),
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));
    }
}
