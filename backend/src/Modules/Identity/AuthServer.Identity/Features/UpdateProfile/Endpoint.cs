using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Http;
using AuthServer.Shared;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace AuthServer.Identity.Features.UpdateProfile;

internal sealed class Endpoint(UserManager<ApplicationUser> users) : Endpoint<UpdateProfileRequest>
{
    public override void Configure()
    {
        Put("/identity/me");
        Policies(IdentityPolicies.Account);
        Options(route => route.WithTags("Identity").WithName("UpdateProfile"));
        Summary(summary =>
        {
            summary.Summary = "Change your user name.";
            summary.Description = "The e-mail is what you sign in with: only an admin changes it. Tokens show the new name from the next renewal.";
            summary.Responses[204] = "Changed.";
        });
    }

    public override async Task HandleAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        if (await users.GetUserAsync(User) is not { } user)
        {
            await Send.UnauthorizedAsync(cancellationToken);
            return;
        }

        var userName = AccountFields.UserNameOf(request.UserName);

        if (!userName.IsSuccess)
        {
            await RefuseAsync(userName.Error, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        if (await users.FindByNameAsync(userName.Value) is { } holder && holder.Id != user.Id)
        {
            await RefuseAsync(IdentityErrors.UserNameTaken, StatusCodes.Status409Conflict, cancellationToken);
            return;
        }

        user.UserName = userName.Value;

        // UpdateAsync normalizes the name and the unique index answers a race.
        if (!(await users.UpdateAsync(user)).Succeeded)
        {
            await RefuseAsync(IdentityErrors.UserNameTaken, StatusCodes.Status409Conflict, cancellationToken);
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }

    private Task RefuseAsync(Error error, int status, CancellationToken cancellationToken) =>
        IdentityProblem.SendAsync(HttpContext, error, status, cancellationToken);
}

internal sealed record UpdateProfileRequest(string? UserName);
