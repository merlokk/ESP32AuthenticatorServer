using AuthServer.Identity.Contracts;
using AuthServer.Sync.Domain;
using AuthServer.Sync.Infrastructure.Http;
using AuthServer.Sync.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Sync.Features.UploadFile;

/// <summary>
/// A device's copy of one store. One short transaction: upsert the store's row, lock it, compare
/// the counter with the head, keep the file, drop the versions beyond the last N. Two devices racing
/// with one counter queue on the row lock: the first is kept, the second sees the new head.
/// </summary>
internal sealed class Endpoint(SyncDbContext context, SyncSettings settings, TimeProvider clock) : EndpointWithoutRequest<UploadFileResponse>
{
    public override void Configure()
    {
        Post("/sync/device/files");
        Policies(IdentityPolicies.Device);
        Options(route => route
            .WithTags("Sync")
            .WithName("UploadFile")
            .Accepts<byte[]>("application/octet-stream"));
        Summary(summary =>
        {
            summary.Summary = "Keep a device's copy of a store if its counter is above the server's (device).";
            summary.Description =
                "The body is the store file as is (header, payload, tag). The counter decides, never the time. "
                + "409 sync.not-newer names the head (version, modified): write max + 1 and try again.";
            summary.Responses[200] = "Kept: it is now the newest.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var file = await ReadBodyAsync(cancellationToken);

        if (file is null)
        {
            await SyncProblem.SendAsync(HttpContext, SyncErrors.FileTooLarge, StatusCodes.Status413PayloadTooLarge, cancellationToken);
            return;
        }

        var parsed = StoreHeader.Parse(file);

        if (!parsed.IsSuccess)
        {
            await SyncProblem.SendAsync(HttpContext, parsed.Error!, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        var header = parsed.Value!;
        var owner = Callers.Owner(User);
        var now = clock.GetUtcNow();
        short kind = (short)header.Kind;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // The row exists before it is locked; head_version -1 is "nothing yet" and never committed alone.
        await context.Database.ExecuteSqlAsync(
            $"""
            insert into sync.stores (id, user_id, store_id, seed_id, kind, head_version, updated_at)
            values ({Guid.CreateVersion7()}, {owner}, {header.StoreId}, {header.SeedId}, {kind}, {-1L}, {now})
            on conflict (user_id, store_id, seed_id, kind) do nothing
            """,
            cancellationToken);

        var store = await context.Stores
            .FromSql(
                $"""
                select * from sync.stores
                where user_id = {owner} and store_id = {header.StoreId} and seed_id = {header.SeedId} and kind = {kind}
                for update
                """)
            .SingleAsync(cancellationToken);

        uint? head = store.HeadVersion < 0 ? null : (uint)store.HeadVersion;

        if (!UploadRule.Accepts(header.Version, head))
        {
            var modified = await context.Versions
                .Where(version => version.StoreRef == store.Id && version.Version == store.HeadVersion)
                .Select(version => version.Modified)
                .SingleAsync(cancellationToken);

            await transaction.RollbackAsync(cancellationToken);
            await SyncProblem.SendAsync(
                HttpContext,
                SyncErrors.NotNewer,
                StatusCodes.Status409Conflict,
                cancellationToken,
                new Dictionary<string, object?> { ["version"] = store.HeadVersion, ["modified"] = modified });
            return;
        }

        context.Versions.Add(new StoreVersion
        {
            Id = Guid.CreateVersion7(),
            StoreRef = store.Id,
            Version = header.Version,
            Modified = header.Modified,
            Cipher = header.Cipher,
            Length = header.Length,
            KdfIterations = header.KdfIterations,
            KeyId = header.KeyId,
            UploadedByDevice = Callers.Device(User),
            UploadedAt = now,
            Content = file,
        });

        store.HeadVersion = header.Version;
        store.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);

        await context.Database.ExecuteSqlAsync(
            $"""
            delete from sync.store_versions
            where store_ref = {store.Id}
              and id not in (select id from sync.store_versions where store_ref = {store.Id} order by version desc limit {settings.VersionsToKeep})
            """,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        await Send.OkAsync(new UploadFileResponse(header.Version, header.Modified), cancellationToken);
    }

    /// <summary>The body, or null past the limit (read no further than one byte over it).</summary>
    private async Task<byte[]?> ReadBodyAsync(CancellationToken cancellationToken)
    {
        if (HttpContext.Request.ContentLength > settings.MaxFileBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;

        while ((read = await HttpContext.Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);

            if (buffer.Length > settings.MaxFileBytes)
            {
                return null;
            }
        }

        return buffer.ToArray();
    }
}

internal sealed record UploadFileResponse(long Version, long Modified);
