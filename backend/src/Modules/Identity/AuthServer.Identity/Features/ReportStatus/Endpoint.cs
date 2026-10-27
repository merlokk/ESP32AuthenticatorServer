using System.Text.Json;
using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Http;
using AuthServer.Identity.Infrastructure.Persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Features.ReportStatus;

/// <summary>
/// A device's periodic report: kept as sent (any fields the firmware adds), its firmware version
/// read out, the device marked seen. The answer names a newer firmware release, if any.
/// </summary>
internal sealed class Endpoint(IdentityDbContext context, IdentitySettings settings, TimeProvider clock) : EndpointWithoutRequest<ReportStatusResponse>
{
    private const int MaximumBytes = 4096;

    public override void Configure()
    {
        Post("/device/status");
        Policies(IdentityPolicies.Device);
        Options(route => route.WithTags("Device").WithName("ReportStatus").Accepts<ReportStatusRequest>("application/json"));
        Summary(summary =>
        {
            summary.Summary = "Report the device's state; learn of a newer firmware release (device).";
            summary.Description =
                "A JSON object of at most 4 KB. firmware is required; the documented fields and any other are kept as sent. "
                + "update: the newest release when it is above firmware, else null. time: the server's clock, Unix seconds.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var report = await ReadAsync(cancellationToken);

        if (report is null)
        {
            await IdentityProblem.SendAsync(HttpContext, IdentityErrors.DeviceStatusInvalid, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        var id = Guid.Parse(User.FindFirst(IdentityClaims.DeviceId)!.Value);

        if (await context.Devices.SingleOrDefaultAsync(device => device.Id == id, cancellationToken) is not { } device)
        {
            await Send.UnauthorizedAsync(cancellationToken);
            return;
        }

        var now = clock.GetUtcNow();
        device.Firmware = report.Value.Firmware;
        device.Status = report.Value.Json;
        device.LastSeenAt = now;
        await context.SaveChangesAsync(cancellationToken);

        await Send.OkAsync(
            new ReportStatusResponse(FirmwareVersion.Update(report.Value.Firmware, settings.LatestFirmware), now.ToUnixTimeSeconds()),
            cancellationToken);
    }

    /// <summary>The report and its firmware, or null when it is not a small JSON object with one.</summary>
    private async Task<(string Firmware, string Json)?> ReadAsync(CancellationToken cancellationToken)
    {
        if (HttpContext.Request.ContentLength > MaximumBytes)
        {
            return null;
        }

        var buffer = new byte[MaximumBytes + 1];
        var length = 0;
        int read;

        while (length < buffer.Length && (read = await HttpContext.Request.Body.ReadAsync(buffer.AsMemory(length), cancellationToken)) > 0)
        {
            length += read;
        }

        if (length > MaximumBytes)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(buffer.AsMemory(0, length));
            var root = document.RootElement;

            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("firmware", out var firmware)
                && firmware.ValueKind == JsonValueKind.String
                && FirmwareVersion.IsValid(firmware.GetString())
                    ? (firmware.GetString()!, root.GetRawText())
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The documented fields; the firmware may add others, which are kept as sent.</summary>
/// <param name="Firmware">git describe of the firmware, e.g. 0.5.0-174-g2f84ffa.</param>
/// <param name="Mac">Wi-Fi MAC, aa:bb:cc:dd:ee:ff.</param>
/// <param name="Wifi">Wi-Fi on.</param>
/// <param name="Ble">Bluetooth LE (FIDO2 over BLE) on.</param>
/// <param name="Battery">Percent, 0 to 100.</param>
/// <param name="Uptime">Seconds since boot.</param>
/// <param name="Time">The device's clock, Unix seconds (0: no believable time).</param>
internal sealed record ReportStatusRequest(
    string Firmware,
    string? Mac,
    bool? Wifi,
    bool? Ble,
    int? Battery,
    long? Uptime,
    long? Time);

/// <param name="Update">The newest firmware release when it is above the reported one; else null.</param>
/// <param name="Time">The server's clock, Unix seconds.</param>
internal sealed record ReportStatusResponse(string? Update, long Time);
