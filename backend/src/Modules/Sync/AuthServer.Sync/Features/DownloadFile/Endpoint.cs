using System.Globalization;
using AuthServer.Identity.Contracts;
using AuthServer.Sync.Domain;
using AuthServer.Sync.Infrastructure.Http;
using AuthServer.Sync.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Sync.Features.DownloadFile;

internal sealed class Endpoint(SyncDbContext context) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/device/stores/files/{storeId}/{seedId}/{kind}");
        Policies(IdentityPolicies.Device);
        Options(route => route.WithTags("Sync").WithName("DownloadFile").Produces(200, typeof(byte[]), "application/octet-stream"));
        Summary(summary =>
        {
            summary.Summary = "The newest copy of one of the owner's stores (device).";
            summary.Description =
                "?above= the version the device holds: 204 unless the server's is higher, so a copy at or below it is never "
                + "served (rollback). The device checks the file itself: the server cannot.";
            summary.Responses[200] = "The store file as it was uploaded.";
            summary.Responses[204] = "Nothing above the version the device holds.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var key = StoreKey.Create(Route<string>("storeId"), Route<string>("seedId"), Route<int>("kind", isRequired: false));

        // Parsed here, not by Query<uint?>: that reads a malformed value as absent, and absent serves the head.
        var named = HttpContext.Request.Query.TryGetValue("above", out var text);
        uint? above = null;

        if (named && uint.TryParse(text.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var held))
        {
            above = held;
        }

        if (!key.IsSuccess || (named && above is null))
        {
            await SyncProblem.SendAsync(HttpContext, SyncErrors.RequestInvalid, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        var owner = Callers.Owner(User);
        var (storeId, seedId, kind) = key.Value;

        var head = await context.Stores
            .Where(store => store.UserId == owner && store.StoreId == storeId && store.SeedId == seedId && store.Kind == kind)
            .Join(context.Versions, store => new { store.Id, store.HeadVersion }, version => new { Id = version.StoreRef, HeadVersion = version.Version }, (_, version) => version)
            .SingleOrDefaultAsync(cancellationToken);

        if (head is null)
        {
            await SyncProblem.SendAsync(HttpContext, SyncErrors.StoreNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        if (!UploadRule.Serves((uint)head.Version, above))
        {
            await Send.NoContentAsync(cancellationToken);
            return;
        }

        await Send.BytesAsync(head.Content, contentType: "application/octet-stream", cancellation: cancellationToken);
    }
}
