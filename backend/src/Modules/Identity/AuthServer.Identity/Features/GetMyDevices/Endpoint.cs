using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.GetMyDevices;

internal sealed class Endpoint(UserManager<ApplicationUser> users, IdentityDbContext context) : EndpointWithoutRequest<MyDevicesResponse>
{
    public override void Configure()
    {
        Get("/identity/me/devices");
        Policies(IdentityPolicies.Account);
        Options(route => route.WithTags("Identity").WithName("GetMyDevices"));
        Summary(summary => summary.Summary = "Your devices. Never their keys or TOTP secrets.");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (await users.GetUserAsync(User) is not { } user)
        {
            await Send.UnauthorizedAsync(cancellationToken);
            return;
        }

        var devices = await context.Devices
            .Where(device => device.UserId == user.Id)
            .OrderBy(device => device.RegisteredAt)
            .Select(device => new MyDevice(device.Id, device.HardwareId, device.Mac, device.RegisteredAt, device.LastSeenAt))
            .ToListAsync(cancellationToken);

        await Send.OkAsync(new MyDevicesResponse(devices), cancellationToken);
    }
}

internal sealed record MyDevicesResponse(IReadOnlyList<MyDevice> Devices);

internal sealed record MyDevice(Guid Id, string HardwareId, string Mac, DateTimeOffset RegisteredAt, DateTimeOffset? LastSeenAt);
