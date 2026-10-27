using AuthServer.Identity.Contracts;
using AuthServer.Sync.Domain;
using AuthServer.Sync.Infrastructure.Http;
using AuthServer.Sync.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Sync.Features.GetStoreVersions;

internal sealed class Endpoint(SyncDbContext context) : EndpointWithoutRequest<StoreVersionsResponse>
{
    public override void Configure()
    {
        Get("/sync/stores/{id}/versions");
        Policies(IdentityPolicies.Account);
        Options(route => route.WithTags("Sync").WithName("GetStoreVersions"));
        Summary(summary => summary.Summary = "The copies the server keeps of one of your stores, newest first: header fields only.");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var owner = Callers.Owner(User);
        var id = Route<Guid>("id");

        if (!await context.Stores.AnyAsync(store => store.Id == id && store.UserId == owner, cancellationToken))
        {
            await SyncProblem.SendAsync(HttpContext, SyncErrors.StoreNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        var versions = await context.Versions
            .Where(version => version.StoreRef == id)
            .OrderByDescending(version => version.Version)
            .Select(version => new VersionRow(
                version.Version,
                version.Modified,
                (int)version.Cipher,
                version.Length,
                version.KdfIterations,
                version.KeyId,
                version.UploadedByDevice,
                version.UploadedAt))
            .ToListAsync(cancellationToken);

        await Send.OkAsync(new StoreVersionsResponse(versions), cancellationToken);
    }
}

internal sealed record StoreVersionsResponse(IReadOnlyList<VersionRow> Versions);

internal sealed record VersionRow(
    long Version,
    long Modified,
    int Cipher,
    long Length,
    long KdfIterations,
    string KeyId,
    Guid UploadedByDeviceId,
    DateTimeOffset UploadedAt);
