using AuthServer.Identity.Contracts;
using AuthServer.Sync.Domain;
using AuthServer.Sync.Infrastructure.Http;
using AuthServer.Sync.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Sync.Features.GetDeviceStores;

/// <summary>
/// Every store of the owner the server holds, for a device choosing one to adopt (a new device, or
/// one restored from its seed phrase): the header of the newest copy and when it was synced.
/// </summary>
internal sealed class Endpoint(SyncDbContext context) : EndpointWithoutRequest<DeviceStoresResponse>
{
    public override void Configure()
    {
        Get("/device/stores");
        Policies(IdentityPolicies.Device);
        Options(route => route.WithTags("Sync").WithName("GetDeviceStores"));
        Summary(summary =>
        {
            summary.Summary = "The owner's stores with their newest copy's header and last sync (device).";
            summary.Description =
                "?seedId= (8 hex) keeps the stores of one seed phrase: the only ones the device can open. Newest sync first.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var seedId = Query<string>("seedId", isRequired: false);

        if (seedId is not null && StoreKey.Create("0000000000000000", seedId, (int)StoreKind.Records) is { IsSuccess: false })
        {
            await SyncProblem.SendAsync(HttpContext, SyncErrors.RequestInvalid, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        var owner = Callers.Owner(User);
        var seed = seedId?.ToLowerInvariant();

        var stores = await context.Stores
            .Where(store => store.UserId == owner && store.HeadVersion >= 0 && (seed == null || store.SeedId == seed))
            .Join(context.Versions, store => new { store.Id, store.HeadVersion }, version => new { Id = version.StoreRef, HeadVersion = version.Version },
                (store, head) => new { store, head })
            .OrderByDescending(pair => pair.head.UploadedAt)
            .Select(pair => new DeviceStore(
                pair.store.StoreId,
                pair.store.SeedId,
                (int)pair.store.Kind,
                pair.head.Version,
                pair.head.Modified,
                (int)pair.head.Cipher,
                pair.head.Length,
                pair.head.KdfIterations,
                pair.head.KeyId,
                pair.head.Nonce,
                pair.head.UploadedByDevice,
                pair.head.UploadedAt))
            .ToListAsync(cancellationToken);

        await Send.OkAsync(new DeviceStoresResponse(stores), cancellationToken);
    }
}

internal sealed record DeviceStoresResponse(IReadOnlyList<DeviceStore> Stores);

/// <param name="StoreId">8 bytes, hex: names the store in heads and files.</param>
/// <param name="SeedId">4 bytes, hex: the seed phrase it is under.</param>
/// <param name="Kind">1 fido (passkeys), 2 records (passwords and TOTP).</param>
/// <param name="Version">The newest copy's counter.</param>
/// <param name="Modified">Its time by the writing device's clock, Unix seconds; 0 unknown.</param>
/// <param name="Cipher">1 AES-256-GCM (the server keeps no other).</param>
/// <param name="Length">Payload bytes.</param>
/// <param name="KeyId">Which key encrypted it (hex): another key id than the device's is another PIN.</param>
/// <param name="Nonce">Its GCM nonce (hex): random on every write.</param>
/// <param name="UploadedByDeviceId">The device that wrote it.</param>
/// <param name="UploadedAt">When it reached the server: the store's last sync.</param>
internal sealed record DeviceStore(
    string StoreId,
    string SeedId,
    int Kind,
    long Version,
    long Modified,
    int Cipher,
    long Length,
    long KdfIterations,
    string KeyId,
    string Nonce,
    Guid UploadedByDeviceId,
    DateTimeOffset UploadedAt);
