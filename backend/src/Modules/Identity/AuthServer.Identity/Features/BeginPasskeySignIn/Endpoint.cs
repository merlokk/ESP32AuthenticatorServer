using AuthServer.Identity.Domain;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace AuthServer.Identity.Features.BeginPasskeySignIn;

internal sealed class Endpoint(SignInManager<ApplicationUser> signIn) : EndpointWithoutRequest<BeginPasskeySignInResponse>
{
    public override void Configure()
    {
        Post("/identity/sign-in/passkey/options");
        AllowAnonymous();
        Options(route => route.WithTags("Identity").WithName("BeginPasskeySignIn"));
        Summary(summary =>
        {
            summary.Summary = "Request options for a passkey sign-in.";
            summary.Description =
                "For PublicKeyCredential.parseRequestOptionsFromJSON() and navigator.credentials.get(); the result "
                + "goes to POST /api/identity/sign-in/passkey. No identifier: the credential is discoverable.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken) =>
        await Send.OkAsync(new BeginPasskeySignInResponse(await signIn.MakePasskeyRequestOptionsAsync(null)), cancellationToken);
}

internal sealed record BeginPasskeySignInResponse(string RequestOptions);
