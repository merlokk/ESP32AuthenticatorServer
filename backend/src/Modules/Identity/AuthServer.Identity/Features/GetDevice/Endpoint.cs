using AuthServer.Identity.Contracts;
using AuthServer.Identity.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.GetDevice;

internal sealed class Endpoint(IdentityDbContext context) : EndpointWithoutRequest<DeviceResponse>
{
    public override void Configure()
    {
        Get("/identity/device");
        Policies(IdentityPolicies.Device);
        Options(route => route.WithTags("Identity").WithName("GetDevice"));
        Summary(summary => summary.Summary = "Who the calling device is (device token).");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var id = Guid.Parse(User.FindFirst(IdentityClaims.DeviceId)!.Value);

        var device = await context.Devices
            .Where(entity => entity.Id == id)
            .Select(entity => new DeviceResponse(entity.Id, entity.UserId, entity.HardwareId))
            .SingleOrDefaultAsync(cancellationToken);

        if (device is null)
        {
            await Send.UnauthorizedAsync(cancellationToken);
            return;
        }

        await Send.OkAsync(device, cancellationToken);
    }
}

internal sealed record DeviceResponse(Guid DeviceId, Guid UserId, string HardwareId);
