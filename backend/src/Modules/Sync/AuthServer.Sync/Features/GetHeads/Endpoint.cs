using AuthServer.Identity.Contracts;
using AuthServer.Sync.Domain;
using AuthServer.Sync.Infrastructure.Http;
using AuthServer.Sync.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Sync.Features.GetHeads;

/// <summary>What the server holds of the device's stores, in one request: the device decides alone.</summary>
internal sealed class Endpoint(SyncDbContext context) : Endpoint<GetHeadsRequest, GetHeadsResponse>
{
    private const int MaximumStores = 16;

    public override void Configure()
    {
        Post("/device/stores/heads");
        Policies(IdentityPolicies.Device);
        Options(route => route.WithTags("Sync").WithName("GetHeads"));
        Summary(summary =>
        {
            summary.Summary = "The newest version and time the server holds of each named store (device).";
            summary.Description =
                "Up to 16 stores. version and modified are null for a store the server does not hold. Higher on the device: "
                + "POST /api/device/stores/files; higher here: GET /api/device/stores/files/... ?above=<own version>.";
        });
    }

    public override async Task HandleAsync(GetHeadsRequest request, CancellationToken cancellationToken)
    {
        var asked = request.Stores ?? [];
        var keys = asked.Select(store => StoreKey.Create(store.StoreId, store.SeedId, store.Kind)).ToList();

        if (asked.Count is 0 or > MaximumStores || keys.Any(key => !key.IsSuccess))
        {
            await SyncProblem.SendAsync(HttpContext, SyncErrors.RequestInvalid, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        var owner = Callers.Owner(User);

        // A user has a few stores: all their heads in one query, matched here.
        var heads = await context.Stores
            .Where(store => store.UserId == owner)
            .Join(context.Versions, store => new { store.Id, store.HeadVersion }, version => new { Id = version.StoreRef, HeadVersion = version.Version },
                (store, version) => new { store.StoreId, store.SeedId, store.Kind, version.Version, version.Modified, version.Nonce })
            .ToListAsync(cancellationToken);

        var answer = keys.Select(result =>
        {
            var key = result.Value;
            var head = heads.SingleOrDefault(store => store.StoreId == key.StoreId && store.SeedId == key.SeedId && store.Kind == key.Kind);
            return new StoreHead(key.StoreId, key.SeedId, (int)key.Kind, head?.Version, head?.Modified, head?.Nonce);
        });

        await Send.OkAsync(new GetHeadsResponse([.. answer]), cancellationToken);
    }
}

internal sealed record GetHeadsRequest(IReadOnlyList<StoreName>? Stores);

/// <param name="StoreId">8 bytes, hex.</param>
/// <param name="SeedId">4 bytes, hex.</param>
/// <param name="Kind">1 fido, 2 records (vault).</param>
internal sealed record StoreName(string? StoreId, string? SeedId, int Kind);

internal sealed record GetHeadsResponse(IReadOnlyList<StoreHead> Stores);

/// <param name="Version">Null: the server holds no copy.</param>
/// <param name="Modified">Unix seconds by the uploading device's clock; 0 unknown; null: no copy.</param>
/// <param name="Nonce">The newest copy's GCM nonce (hex): one counter with another nonce is another write.</param>
internal sealed record StoreHead(string StoreId, string SeedId, int Kind, long? Version, long? Modified, string? Nonce);
