using AuthServer.Identity.Contracts;
using AuthServer.Sync.Domain;
using AuthServer.Sync.Infrastructure.Http;
using AuthServer.Sync.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Sync.Features.DeleteStore;

/// <summary>A store and every copy of it (the versions go by the foreign key's cascade).</summary>
internal sealed class Endpoint(SyncDbContext context) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/sync/stores/{id}");
        Policies(IdentityPolicies.Account);
        Options(route => route.WithTags("Sync").WithName("DeleteStore"));
        Summary(summary =>
        {
            summary.Summary = "Delete one of your stores with all its copies.";
            summary.Description = "A device that still holds the store uploads it again on its next sync.";
            summary.Responses[204] = "Deleted.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var owner = Callers.Owner(User);
        var id = Route<Guid>("id", isRequired: false);

        var deleted = await context.Stores
            .Where(store => store.Id == id && store.UserId == owner)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted == 0)
        {
            await SyncProblem.SendAsync(HttpContext, SyncErrors.StoreNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
