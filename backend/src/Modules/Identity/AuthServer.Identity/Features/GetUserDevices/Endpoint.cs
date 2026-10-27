using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Http;
using AuthServer.Identity.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.GetUserDevices;

internal sealed class Endpoint(IdentityDbContext context) : EndpointWithoutRequest<UserDevicesResponse>
{
    public override void Configure()
    {
        Get("/identity/users/{id}/devices");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Identity").WithName("GetUserDevices"));
        Summary(summary => summary.Summary = "A user's devices (admin). Never their keys or TOTP secrets.");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var id = Route<Guid>("id");

        if (!await context.Users.AnyAsync(user => user.Id == id, cancellationToken))
        {
            await IdentityProblem.SendAsync(HttpContext, IdentityErrors.UserNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        var devices = await context.Devices
            .Where(device => device.UserId == id)
            .OrderBy(device => device.RegisteredAt)
            .Select(device => new UserDevice(device.Id, device.Serial, device.Mac, device.RegisteredAt, device.LastSeenAt))
            .ToListAsync(cancellationToken);

        await Send.OkAsync(new UserDevicesResponse(devices), cancellationToken);
    }
}

internal sealed record UserDevicesResponse(IReadOnlyList<UserDevice> Devices);

internal sealed record UserDevice(Guid Id, string Serial, string Mac, DateTimeOffset RegisteredAt, DateTimeOffset? LastSeenAt);
