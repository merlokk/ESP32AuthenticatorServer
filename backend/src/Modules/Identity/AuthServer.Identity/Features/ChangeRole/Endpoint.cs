using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Accounts;
using AuthServer.Identity.Infrastructure.Http;
using AuthServer.Identity.Infrastructure.Persistence;
using AuthServer.Shared;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.ChangeRole;

internal sealed class Endpoint(IdentityDbContext context, AccountChanges changes) : Endpoint<ChangeRoleRequest>
{
    public override void Configure()
    {
        Put("/identity/users/{id}/role");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Identity").WithName("ChangeRole"));
        Summary(summary =>
        {
            summary.Summary = "Make a user an admin or a user (admin).";
            summary.Description =
                "A promotion reaches the next token renewal; a demotion ends the user's sessions. The last active admin "
                + "keeps the role (admin.last).";
        });
    }

    public override async Task HandleAsync(ChangeRoleRequest request, CancellationToken cancellationToken)
    {
        if (UserRoles.Of(request.Role) is not { } role)
        {
            await RefuseAsync(IdentityErrors.RoleInvalid, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        var id = Route<Guid>("id");

        await using var transaction = await changes.BeginAsync(cancellationToken);

        if (await context.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken) is not { } user)
        {
            await RefuseAsync(IdentityErrors.UserNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        if (await changes.LeavesNoAdminAsync(user, staysActiveAdmin: role == UserRoles.Admin && user.BlockedAt is null, cancellationToken))
        {
            await RefuseAsync(IdentityErrors.LastAdmin, StatusCodes.Status409Conflict, cancellationToken);
            return;
        }

        var demoted = user.Role == UserRoles.Admin && role != UserRoles.Admin;
        user.Role = role;
        await context.SaveChangesAsync(cancellationToken);

        if (demoted)
        {
            await changes.RevokeSessionsAsync(user.Id, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        await Send.NoContentAsync(cancellationToken);
    }

    private Task RefuseAsync(Error error, int status, CancellationToken cancellationToken) =>
        IdentityProblem.SendAsync(HttpContext, error, status, cancellationToken);
}

internal sealed record ChangeRoleRequest(string? Role);
