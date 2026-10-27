using AuthServer.Firmware.Domain;
using AuthServer.Firmware.Infrastructure.Http;
using AuthServer.Firmware.Infrastructure.Persistence;
using AuthServer.Identity.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Firmware.Features.DeleteRelease;

internal sealed class Endpoint(FirmwareDbContext context) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/firmware/releases/{id}");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Firmware").WithName("DeleteRelease"));
        Summary(summary =>
        {
            summary.Summary = "Delete a release and its image (admin).";
            summary.Description = "An active one too: its board then offers none. A device fetching it gets 404 and asks again.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var id = Route<Guid>("id");

        if (await context.Releases.Where(release => release.Id == id).ExecuteDeleteAsync(cancellationToken) == 0)
        {
            await FirmwareProblem.SendAsync(HttpContext, FirmwareErrors.ReleaseNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
