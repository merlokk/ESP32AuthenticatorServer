using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Http;
using AuthServer.Identity.Infrastructure.Persistence;
using AuthServer.Shared;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.CompletePasskeyRegistration;

internal sealed class Endpoint(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IdentityDbContext context) : Endpoint<CompletePasskeyRegistrationRequest>
{
    public override void Configure()
    {
        Post("/identity/me/passkeys");
        Policies(IdentityPolicies.Account);
        Options(route => route.WithTags("Identity").WithName("CompletePasskeyRegistration"));
        Summary(summary =>
        {
            summary.Summary = "Register the passkey the browser just created.";
            summary.Description = "credentialJson is JSON.stringify() of the browser's result, unaltered.";
            summary.Responses[204] = "Registered.";
        });
    }

    public override async Task HandleAsync(CompletePasskeyRegistrationRequest request, CancellationToken cancellationToken)
    {
        if (await users.GetUserAsync(User) is not { } user)
        {
            await Send.UnauthorizedAsync(cancellationToken);
            return;
        }

        var name = AccountFields.PasskeyNameOf(request.Name);

        if (!name.IsSuccess)
        {
            await RefuseAsync(name.Error, cancellationToken);
            return;
        }

        if (await context.UserPasskeys.CountAsync(passkey => passkey.UserId == user.Id, cancellationToken) >= AccountFields.MaximumPasskeys)
        {
            await RefuseAsync(IdentityErrors.PasskeyLimitReached, cancellationToken);
            return;
        }

        // Before the attestation, which refuses a known credential with a generic failure.
        if (PasskeyJson.CredentialIdOf(request.CredentialJson) is { } credentialId
            && await context.UserPasskeys.AnyAsync(passkey => passkey.CredentialId == credentialId, cancellationToken))
        {
            await RefuseAsync(IdentityErrors.PasskeyAlreadyRegistered, cancellationToken);
            return;
        }

        PasskeyAttestationResult attestation;

        try
        {
            attestation = await signIn.PerformPasskeyAttestationAsync(request.CredentialJson ?? string.Empty);
        }
        catch (InvalidOperationException)
        {
            // No ceremony underway: its state cookie did not come back with this request.
            await RefuseAsync(IdentityErrors.PasskeyRegistrationFailed, cancellationToken);
            return;
        }

        if (!attestation.Succeeded || attestation.Passkey is null)
        {
            await RefuseAsync(IdentityErrors.PasskeyRegistrationFailed, cancellationToken);
            return;
        }

        attestation.Passkey.Name = name.Value;

        if (!(await users.AddOrUpdatePasskeyAsync(user, attestation.Passkey)).Succeeded)
        {
            await RefuseAsync(IdentityErrors.PasskeyRegistrationFailed, cancellationToken);
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }

    private Task RefuseAsync(Error error, CancellationToken cancellationToken) =>
        IdentityProblem.SendAsync(HttpContext, error, StatusCodes.Status400BadRequest, cancellationToken);
}

internal sealed record CompletePasskeyRegistrationRequest(string? CredentialJson, string? Name);
