using System.Text.Json;
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
            .Select(device => new { device.Id, device.Serial, device.Mac, device.RegisteredAt, device.LastSeenAt, device.Firmware, device.Status })
            .ToListAsync(cancellationToken);

        // The report is kept as text (jsonb) and handed back as the object the device sent.
        await Send.OkAsync(
            new UserDevicesResponse([.. devices.Select(device => new UserDevice(
                device.Id,
                device.Serial,
                device.Mac,
                device.RegisteredAt,
                device.LastSeenAt,
                device.Firmware,
                device.Status is null ? null : JsonDocument.Parse(device.Status).RootElement.Clone()))]),
            cancellationToken);
    }
}

internal sealed record UserDevicesResponse(IReadOnlyList<UserDevice> Devices);

/// <param name="Firmware">From the last status report (POST /api/device/status); null before one.</param>
/// <param name="Status">The last status report as the device sent it; null before one.</param>
internal sealed record UserDevice(
    Guid Id,
    string Serial,
    string Mac,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? LastSeenAt,
    string? Firmware,
    JsonElement? Status);
