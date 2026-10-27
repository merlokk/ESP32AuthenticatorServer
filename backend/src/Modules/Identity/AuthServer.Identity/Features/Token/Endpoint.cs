using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Persistence;
using AuthServer.Identity.Infrastructure.Tokens;
using FastEndpoints;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthServer.Identity.Features.Token;

/// <summary>
/// OpenIddict's token endpoint (passthrough). OpenIddict has already checked the grant (code and
/// PKCE, refresh token, or the device's client assertion); this decides what the token says,
/// from the account as it is now. A deleted or blocked account gets invalid_grant.
/// </summary>
internal sealed class Endpoint(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IdentityDbContext context,
    TimeProvider clock) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/connect/token");
        RoutePrefixOverride(string.Empty);
        AllowAnonymous();
        Description(builder => builder.ExcludeFromDescription());
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("No OpenIddict request: the server middleware did not run.");

        if (request.IsClientCredentialsGrantType())
        {
            await DeviceAsync(request.ClientId, cancellationToken);
            return;
        }

        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            await RefuseAsync(Errors.UnsupportedGrantType, "Only authorization_code, refresh_token and client_credentials.");
            return;
        }

        var granted = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var user = await users.FindByIdAsync(granted.Principal?.GetClaim(OpenIddictConstants.Claims.Subject) ?? string.Empty);

        if (user is null || !await signIn.CanSignInAsync(user))
        {
            await RefuseAsync(Errors.InvalidGrant, "The account no longer signs in.");
            return;
        }

        await Send.ResultAsync(Results.SignIn(
            TokenPrincipals.ForUser(user, granted.Principal!.GetScopes()),
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));
    }

    // The client id is the device id; OpenIddict has verified the assertion against its key.
    private async Task DeviceAsync(string? clientId, CancellationToken cancellationToken)
    {
        var device = Guid.TryParse(clientId, out var id)
            ? await context.Devices.SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken)
            : null;

        var owner = device is null ? null : await users.FindByIdAsync(device.UserId.ToString());

        if (device is null || owner is null || owner.BlockedAt is not null)
        {
            await RefuseAsync(Errors.InvalidGrant, "The device or its account no longer signs in.");
            return;
        }

        device.LastSeenAt = clock.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);

        await Send.ResultAsync(Results.SignIn(
            TokenPrincipals.ForDevice(device),
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));
    }

    private Task RefuseAsync(string error, string description) =>
        Send.ResultAsync(Results.Forbid(
            authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
            properties: new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
            })));
}
