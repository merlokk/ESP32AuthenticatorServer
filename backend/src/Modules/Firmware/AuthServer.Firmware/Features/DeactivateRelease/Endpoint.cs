using AuthServer.Firmware.Domain;
using AuthServer.Firmware.Infrastructure.Http;
using AuthServer.Firmware.Infrastructure.Persistence;
using AuthServer.Identity.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Firmware.Features.DeactivateRelease;

internal sealed class Endpoint(FirmwareDbContext context) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/firmware/releases/{id}/active");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Firmware").WithName("DeactivateRelease"));
        Summary(summary =>
        {
            summary.Summary = "Stop offering this release (admin).";
            summary.Description = "Its board then has no active release: no device is offered an update.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var id = Route<Guid>("id");

        var changed = await context.Releases
            .Where(release => release.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(release => release.Active, false), cancellationToken);

        if (changed == 0)
        {
            await FirmwareProblem.SendAsync(HttpContext, FirmwareErrors.ReleaseNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
