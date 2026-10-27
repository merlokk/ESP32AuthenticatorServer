using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Accounts;
using AuthServer.Identity.Infrastructure.Devices;
using AuthServer.Identity.Infrastructure.Http;
using AuthServer.Identity.Infrastructure.Persistence;
using AuthServer.Shared;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.DeleteUser;

internal sealed class Endpoint(IdentityDbContext context, AccountChanges changes, DeviceClients clients) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/identity/users/{id}");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Identity").WithName("DeleteUser"));
        Summary(summary =>
        {
            summary.Summary = "Delete a user with their devices and passkeys (admin).";
            summary.Description = "Ends their sessions and their devices' clients. The last active admin cannot be deleted (admin.last).";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var id = Route<Guid>("id");

        await using var transaction = await changes.BeginAsync(cancellationToken);

        if (await context.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken) is not { } user)
        {
            await RefuseAsync(IdentityErrors.UserNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        if (await changes.LeavesNoAdminAsync(user, staysActiveAdmin: false, cancellationToken))
        {
            await RefuseAsync(IdentityErrors.LastAdmin, StatusCodes.Status409Conflict, cancellationToken);
            return;
        }

        foreach (var deviceId in await context.Devices.Where(device => device.UserId == id).Select(device => device.Id).ToListAsync(cancellationToken))
        {
            await clients.DeleteAsync(deviceId, cancellationToken);
        }

        await changes.RevokeSessionsAsync(id, cancellationToken);

        // Devices and passkeys go with the row (cascade).
        context.Users.Remove(user);
        await context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        await Send.NoContentAsync(cancellationToken);
    }

    private Task RefuseAsync(Error error, int status, CancellationToken cancellationToken) =>
        IdentityProblem.SendAsync(HttpContext, error, status, cancellationToken);
}
