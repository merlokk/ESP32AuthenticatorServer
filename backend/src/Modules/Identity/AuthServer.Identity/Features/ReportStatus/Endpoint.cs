using System.Text.Json;
using AuthServer.Firmware.Contracts;
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
/// read out, the device marked seen. The answer names the board's active firmware release when the
/// device runs another version.
/// </summary>
internal sealed class Endpoint(IdentityDbContext context, IFirmwareOffers firmware, TimeProvider clock) : EndpointWithoutRequest<ReportStatusResponse>
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
                "A JSON object of at most 4 KB. firmware is required, board names the build target; the documented fields and any "
                + "other are kept as sent. update: the board's active release when firmware is another version, else null. "
                + "time: the server's clock, Unix seconds.";
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

        var offer = report.Value.Board is { } board
            ? await firmware.OfferAsync(board, report.Value.Firmware, cancellationToken)
            : null;

        await Send.OkAsync(
            new ReportStatusResponse(
                offer is null ? null : new FirmwareUpdate(offer.Id, offer.Version, offer.Size, offer.Sha256),
                now.ToUnixTimeSeconds()),
            cancellationToken);
    }

    /// <summary>
    /// The report, its firmware and its board (null when missing or not one), or null when it is
    /// not a small JSON object with a firmware version.
    /// </summary>
    private async Task<(string Firmware, string? Board, string Json)?> ReadAsync(CancellationToken cancellationToken)
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

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("firmware", out var firmware)
                || firmware.ValueKind != JsonValueKind.String
                || !FirmwareVersion.IsValid(firmware.GetString()))
            {
                return null;
            }

            var board = root.TryGetProperty("board", out var named) && named.ValueKind == JsonValueKind.String
                && FirmwareVersion.IsValidBoard(named.GetString())
                    ? named.GetString()
                    : null;

            return (firmware.GetString()!, board, root.GetRawText());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The documented fields; the firmware may add others, which are kept as sent.</summary>
/// <param name="Firmware">git describe of the firmware, e.g. 0.5.0-174-g2f84ffa.</param>
/// <param name="Board">The build target: x4pro, dev. Without it nothing is offered.</param>
/// <param name="Mac">Wi-Fi MAC, aa:bb:cc:dd:ee:ff.</param>
/// <param name="Wifi">Wi-Fi on.</param>
/// <param name="Ble">Bluetooth LE (FIDO2 over BLE) on.</param>
/// <param name="Battery">Percent, 0 to 100.</param>
/// <param name="Uptime">Seconds since boot.</param>
/// <param name="Time">The device's clock, Unix seconds (0: no believable time).</param>
internal sealed record ReportStatusRequest(
    string Firmware,
    string? Board,
    string? Mac,
    bool? Wifi,
    bool? Ble,
    int? Battery,
    long? Uptime,
    long? Time);

/// <param name="Update">The board's active release when the device runs another version; else null.</param>
/// <param name="Time">The server's clock, Unix seconds.</param>
internal sealed record ReportStatusResponse(FirmwareUpdate? Update, long Time);

/// <param name="Id">GET /api/device/firmware/{id}/image fetches it, in pieces (Range).</param>
/// <param name="Size">Bytes of the image.</param>
/// <param name="Sha256">Of the whole image, lower-case hex.</param>
internal sealed record FirmwareUpdate(Guid Id, string Version, long Size, string Sha256);
