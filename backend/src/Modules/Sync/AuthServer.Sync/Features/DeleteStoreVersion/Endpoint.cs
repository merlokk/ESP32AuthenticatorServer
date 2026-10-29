using AuthServer.Identity.Contracts;
using AuthServer.Sync.Domain;
using AuthServer.Sync.Infrastructure.Http;
using AuthServer.Sync.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Sync.Features.DeleteStoreVersion;

/// <summary>
/// One copy of a store, under the store's row lock as an upload takes it. The newest one gone, the
/// copy below is the head; the last one gone, the store goes too.
/// </summary>
internal sealed class Endpoint(SyncDbContext context) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/sync/stores/{id}/versions/{version}");
        Policies(IdentityPolicies.Account);
        Options(route => route.WithTags("Sync").WithName("DeleteStoreVersion"));
        Summary(summary =>
        {
            summary.Summary = "Delete one copy of one of your stores; the last one deletes the store.";
            summary.Description =
                "The newest one deleted, the copy below is what devices get; a device that holds more uploads it again.";
            summary.Responses[204] = "Deleted.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var owner = Callers.Owner(User);
        var id = Route<Guid>("id", isRequired: false);
        var number = Route<long>("version", isRequired: false);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var store = await context.Stores
            .FromSql($"select * from sync.stores where id = {id} and user_id = {owner} for update")
            .SingleOrDefaultAsync(cancellationToken);

        if (store is null)
        {
            await SyncProblem.SendAsync(HttpContext, SyncErrors.StoreNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        var deleted = await context.Versions
            .Where(version => version.StoreRef == store.Id && version.Version == number)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted == 0)
        {
            await SyncProblem.SendAsync(HttpContext, SyncErrors.VersionNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        if (number == store.HeadVersion)
        {
            var below = await context.Versions
                .Where(version => version.StoreRef == store.Id)
                .MaxAsync(version => (long?)version.Version, cancellationToken);

            if (below is { } head)
            {
                store.HeadVersion = head;
                await context.SaveChangesAsync(cancellationToken);
            }
            else
            {
                context.Stores.Remove(store);
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        await Send.NoContentAsync(cancellationToken);
    }
}
