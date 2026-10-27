using AuthServer.Identity.Contracts;
using AuthServer.Sync.Infrastructure.Http;
using AuthServer.Sync.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Sync.Features.GetStores;

internal sealed class Endpoint(SyncDbContext context) : EndpointWithoutRequest<StoresResponse>
{
    public override void Configure()
    {
        Get("/sync/stores");
        Policies(IdentityPolicies.Account);
        Options(route => route.WithTags("Sync").WithName("GetStores"));
        Summary(summary =>
        {
            summary.Summary = "Your synced stores and their newest copy: header fields only, never the file.";
            summary.Description = "uploadedByDeviceId is a device of yours (GET /api/identity/me/devices), or one removed since.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var owner = Callers.Owner(User);

        var stores = await context.Stores
            .Where(store => store.UserId == owner && store.HeadVersion >= 0)
            .OrderBy(store => store.Kind)
            .ThenBy(store => store.StoreId)
            .ThenBy(store => store.SeedId)
            .Join(context.Versions, store => new { store.Id, store.HeadVersion }, version => new { Id = version.StoreRef, HeadVersion = version.Version },
                (store, head) => new StoreRow(
                    store.Id,
                    store.StoreId,
                    store.SeedId,
                    (int)store.Kind,
                    head.Version,
                    head.Modified,
                    (int)head.Cipher,
                    head.Length,
                    head.KdfIterations,
                    head.KeyId,
                    head.UploadedByDevice,
                    head.UploadedAt,
                    context.Versions.Count(version => version.StoreRef == store.Id)))
            .ToListAsync(cancellationToken);

        await Send.OkAsync(new StoresResponse(stores), cancellationToken);
    }
}

internal sealed record StoresResponse(IReadOnlyList<StoreRow> Stores);

/// <param name="Kind">1 fido (passkeys), 2 records (passwords and TOTP).</param>
/// <param name="Cipher">0 plain, 1 AES-256-GCM.</param>
/// <param name="Modified">Unix seconds by the device's clock; 0 unknown.</param>
/// <param name="KeyId">Which key encrypted it: another key id on another copy is a PIN or seed change.</param>
/// <param name="Versions">Copies the server keeps (Sync:VersionsToKeep at most).</param>
internal sealed record StoreRow(
    Guid Id,
    string StoreId,
    string SeedId,
    int Kind,
    long Version,
    long Modified,
    int Cipher,
    long Length,
    long KdfIterations,
    string KeyId,
    Guid UploadedByDeviceId,
    DateTimeOffset UploadedAt,
    int Versions);
