using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Http;
using AuthServer.Identity.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.CompletePasskeySignIn;

internal sealed class Endpoint(
    SignInManager<ApplicationUser> signIn,
    IdentityDbContext context,
    TimeProvider clock) : Endpoint<CompletePasskeySignInRequest, CompletePasskeySignInResponse>
{
    public override void Configure()
    {
        Post("/identity/sign-in/passkey");
        AllowAnonymous();
        Options(route => route.WithTags("Identity").WithName("CompletePasskeySignIn"));
        Summary(summary =>
        {
            summary.Summary = "Sign in with the assertion the browser produced.";
            summary.Description =
                "Sets the same cookie as the TOTP sign-in. Every refusal is the same 401 as the TOTP path.";
            summary.Responses[200] = "Signed in. The credential is the Set-Cookie header.";
        });
    }

    public override async Task HandleAsync(CompletePasskeySignInRequest request, CancellationToken cancellationToken)
    {
        var credentialJson = request.CredentialJson ?? string.Empty;
        var result = await signIn.PasskeySignInAsync(credentialJson);

        if (!result.Succeeded)
        {
            await IdentityProblem.SendAsync(HttpContext, IdentityErrors.SignInRefused, StatusCodes.Status401Unauthorized, cancellationToken);
            return;
        }

        if (PasskeyJson.CredentialIdOf(credentialJson) is { } credentialId)
        {
            await context.UserPasskeys
                .Where(credential => credential.CredentialId == credentialId)
                .ExecuteUpdateAsync(update => update.SetProperty(credential => credential.LastUsedAt, clock.GetUtcNow()), cancellationToken);
        }

        await Send.OkAsync(new CompletePasskeySignInResponse(SignedIn: true), cancellationToken);
    }
}

internal sealed record CompletePasskeySignInRequest(string? CredentialJson);

internal sealed record CompletePasskeySignInResponse(bool SignedIn);
