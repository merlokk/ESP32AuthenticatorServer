using System.Text.Json;
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
            .Select(device => new { device.Id, device.Name, device.Serial, device.Mac, device.RegisteredAt, device.LastSeenAt, device.Firmware, device.Status })
            .ToListAsync(cancellationToken);

        // The report is kept as text (jsonb) and handed back as the object the device sent.
        await Send.OkAsync(
            new MyDevicesResponse([.. devices.Select(device => new MyDevice(
                device.Id,
                device.Name,
                device.Serial,
                device.Mac,
                device.RegisteredAt,
                device.LastSeenAt,
                device.Firmware,
                device.Status is null ? null : JsonDocument.Parse(device.Status).RootElement.Clone()))]),
            cancellationToken);
    }
}

internal sealed record MyDevicesResponse(IReadOnlyList<MyDevice> Devices);

/// <param name="Name">What its owner calls it; null: unnamed.</param>
/// <param name="Firmware">From the last status report (POST /api/device/status); null before one.</param>
/// <param name="Status">The last status report as the device sent it; null before one.</param>
internal sealed record MyDevice(
    Guid Id,
    string? Name,
    string Serial,
    string Mac,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? LastSeenAt,
    string? Firmware,
    JsonElement? Status);
