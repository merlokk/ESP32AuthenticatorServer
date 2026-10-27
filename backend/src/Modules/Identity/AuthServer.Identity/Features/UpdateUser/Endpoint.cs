using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Http;
using AuthServer.Shared;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace AuthServer.Identity.Features.UpdateUser;

internal sealed class Endpoint(UserManager<ApplicationUser> users) : Endpoint<UpdateUserRequest>
{
    public override void Configure()
    {
        Put("/identity/users/{id}");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Identity").WithName("UpdateUser"));
        Summary(summary => summary.Summary = "Change a user's name and e-mail (admin). The e-mail is what they sign in with.");
    }

    public override async Task HandleAsync(UpdateUserRequest request, CancellationToken cancellationToken)
    {
        if (await users.FindByIdAsync(Route<Guid>("id").ToString()) is not { } user)
        {
            await RefuseAsync(IdentityErrors.UserNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        var email = AccountFields.EmailOf(request.Email);
        var userName = AccountFields.UserNameOf(request.UserName);

        if (!email.IsSuccess || !userName.IsSuccess)
        {
            await RefuseAsync(!email.IsSuccess ? email.Error : userName.Error!, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        if (await users.FindByEmailAsync(email.Value) is { } owner && owner.Id != user.Id)
        {
            await RefuseAsync(IdentityErrors.EmailTaken, StatusCodes.Status409Conflict, cancellationToken);
            return;
        }

        if (await users.FindByNameAsync(userName.Value) is { } holder && holder.Id != user.Id)
        {
            await RefuseAsync(IdentityErrors.UserNameTaken, StatusCodes.Status409Conflict, cancellationToken);
            return;
        }

        user.Email = email.Value;
        user.UserName = userName.Value;

        // UpdateAsync normalizes both and the unique indexes answer a race.
        if (!(await users.UpdateAsync(user)).Succeeded)
        {
            await RefuseAsync(IdentityErrors.EmailTaken, StatusCodes.Status409Conflict, cancellationToken);
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }

    private Task RefuseAsync(Error error, int status, CancellationToken cancellationToken) =>
        IdentityProblem.SendAsync(HttpContext, error, status, cancellationToken);
}

internal sealed record UpdateUserRequest(string? UserName, string? Email);
