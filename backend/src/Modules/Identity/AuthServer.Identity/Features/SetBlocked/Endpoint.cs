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

namespace AuthServer.Identity.Features.SetBlocked;

internal sealed class Endpoint(IdentityDbContext context, AccountChanges changes, TimeProvider clock) : Endpoint<SetBlockedRequest>
{
    public override void Configure()
    {
        Put("/identity/users/{id}/blocked");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Identity").WithName("SetBlocked"));
        Summary(summary =>
        {
            summary.Summary = "Block or unblock a user (admin).";
            summary.Description =
                "A blocked user cannot sign in, renew, register a device or sign a device in; blocking ends their "
                + "sessions. The last active admin cannot be blocked (admin.last).";
        });
    }

    public override async Task HandleAsync(SetBlockedRequest request, CancellationToken cancellationToken)
    {
        var id = Route<Guid>("id");

        await using var transaction = await changes.BeginAsync(cancellationToken);

        if (await context.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken) is not { } user)
        {
            await RefuseAsync(IdentityErrors.UserNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        if (request.Blocked && await changes.LeavesNoAdminAsync(user, staysActiveAdmin: false, cancellationToken))
        {
            await RefuseAsync(IdentityErrors.LastAdmin, StatusCodes.Status409Conflict, cancellationToken);
            return;
        }

        if (request.Blocked && user.BlockedAt is null)
        {
            user.BlockedAt = clock.GetUtcNow();
            await context.SaveChangesAsync(cancellationToken);
            await changes.RevokeSessionsAsync(user.Id, cancellationToken);
        }
        else if (!request.Blocked && user.BlockedAt is not null)
        {
            user.BlockedAt = null;
            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        await Send.NoContentAsync(cancellationToken);
    }

    private Task RefuseAsync(Error error, int status, CancellationToken cancellationToken) =>
        IdentityProblem.SendAsync(HttpContext, error, status, cancellationToken);
}

internal sealed record SetBlockedRequest(bool Blocked);
