using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Devices;
using AuthServer.Identity.Infrastructure.Http;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AuthServer.Identity.Features.RemoveUserDevice;

internal sealed class Endpoint(DeviceRemoval removal) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/identity/users/{userId}/devices/{id}");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Identity").WithName("RemoveUserDevice"));
        Summary(summary =>
        {
            summary.Summary = "Remove a user's device (admin); it signs in no more.";
            summary.Description = "Its token lapses within minutes; the user and their stores stay. device.not-found: not this user's.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (!await removal.RemoveAsync(Route<Guid>("userId", isRequired: false), Route<Guid>("id", isRequired: false), cancellationToken))
        {
            await IdentityProblem.SendAsync(HttpContext, IdentityErrors.DeviceNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        await Send.NoContentAsync(cancellationToken);
    }
}
