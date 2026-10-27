using AuthServer.Identity.Domain;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Server.AspNetCore;

namespace AuthServer.Identity.Features.EndSession;

/// <summary>OpenIddict's end-session endpoint (passthrough): drops the identity cookie.</summary>
internal sealed class Endpoint(SignInManager<ApplicationUser> signIn) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Routes("/connect/endsession");
        Verbs(Http.GET, Http.POST);
        RoutePrefixOverride(string.Empty);
        AllowAnonymous();
        Description(builder => builder.ExcludeFromDescription());
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        await signIn.SignOutAsync();

        await Send.ResultAsync(Results.SignOut(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]));
    }
}
