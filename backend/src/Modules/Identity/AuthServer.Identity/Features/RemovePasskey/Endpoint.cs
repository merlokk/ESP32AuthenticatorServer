using System.Buffers.Text;
using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Http;
using AuthServer.Identity.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.RemovePasskey;

internal sealed class Endpoint(UserManager<ApplicationUser> users, IdentityDbContext context) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/identity/me/passkeys/{id}");
        Policies(IdentityPolicies.Account);
        Options(route => route.WithTags("Identity").WithName("RemovePasskey"));
        Summary(summary =>
        {
            summary.Summary = "Remove one of your passkeys; it no longer signs in.";
            summary.Description = "Any passkey may go, the last one too: TOTP signs in without one.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (await users.GetUserAsync(User) is not { } user)
        {
            await Send.UnauthorizedAsync(cancellationToken);
            return;
        }

        var id = Route<string>("id") ?? string.Empty;
        var credentialId = Base64Url.IsValid(id) ? Base64Url.DecodeFromChars(id) : null;

        var removed = credentialId is not null && await context.UserPasskeys
            .Where(passkey => passkey.UserId == user.Id && passkey.CredentialId == credentialId)
            .ExecuteDeleteAsync(cancellationToken) == 1;

        if (!removed)
        {
            await IdentityProblem.SendAsync(HttpContext, IdentityErrors.PasskeyNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
