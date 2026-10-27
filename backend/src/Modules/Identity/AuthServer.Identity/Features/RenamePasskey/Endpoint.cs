using System.Buffers.Text;
using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Http;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace AuthServer.Identity.Features.RenamePasskey;

internal sealed class Endpoint(UserManager<ApplicationUser> users) : Endpoint<RenamePasskeyRequest>
{
    public override void Configure()
    {
        Put("/identity/me/passkeys/{id}");
        Policies(IdentityPolicies.Account);
        Options(route => route.WithTags("Identity").WithName("RenamePasskey"));
        Summary(summary =>
        {
            summary.Summary = "Rename one of your passkeys.";
            summary.Responses[204] = "Renamed.";
        });
    }

    public override async Task HandleAsync(RenamePasskeyRequest request, CancellationToken cancellationToken)
    {
        if (await users.GetUserAsync(User) is not { } user)
        {
            await Send.UnauthorizedAsync(cancellationToken);
            return;
        }

        var name = AccountFields.PasskeyNameOf(request.Name);

        if (!name.IsSuccess)
        {
            await IdentityProblem.SendAsync(HttpContext, name.Error, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        var id = Route<string>("id") ?? string.Empty;
        var passkey = Base64Url.IsValid(id) ? await users.GetPasskeyAsync(user, Base64Url.DecodeFromChars(id)) : null;

        if (passkey is null)
        {
            await IdentityProblem.SendAsync(HttpContext, IdentityErrors.PasskeyNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        passkey.Name = name.Value;
        // The passkey was just found on this user: a failure here is not an expected outcome.
        if (!(await users.AddOrUpdatePasskeyAsync(user, passkey)).Succeeded)
        {
            throw new InvalidOperationException("The passkey could not be renamed.");
        }

        await Send.NoContentAsync(cancellationToken);
    }
}

internal sealed record RenamePasskeyRequest(string? Name);
