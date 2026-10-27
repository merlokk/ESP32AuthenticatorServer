using AuthServer.Firmware.Domain;
using AuthServer.Firmware.Infrastructure.Http;
using AuthServer.Firmware.Infrastructure.Persistence;
using AuthServer.Identity.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Firmware.Features.ActivateRelease;

/// <summary>
/// The release becomes its board's active one, the previous one inactive: a rollout, or a rollback
/// when it is older. Two statements in one transaction: the unique index on the active release
/// is checked row by row, so the old one is cleared first.
/// </summary>
internal sealed class Endpoint(FirmwareDbContext context) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Put("/firmware/releases/{id}/active");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Firmware").WithName("ActivateRelease"));
        Summary(summary =>
        {
            summary.Summary = "Offer this release to its board's devices (admin).";
            summary.Description = "The board's previous active release is no longer offered. Devices on another version are offered this one.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var id = Route<Guid>("id");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        if (await context.Releases.Where(release => release.Id == id).Select(release => release.Board).SingleOrDefaultAsync(cancellationToken) is not { } board)
        {
            await FirmwareProblem.SendAsync(HttpContext, FirmwareErrors.ReleaseNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        await context.Releases
            .Where(release => release.Board == board && release.Active && release.Id != id)
            .ExecuteUpdateAsync(set => set.SetProperty(release => release.Active, false), cancellationToken);
        await context.Releases
            .Where(release => release.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(release => release.Active, true), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        await Send.NoContentAsync(cancellationToken);
    }
}
