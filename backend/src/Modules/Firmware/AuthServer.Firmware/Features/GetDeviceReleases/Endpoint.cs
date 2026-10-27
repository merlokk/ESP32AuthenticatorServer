using AuthServer.Firmware.Domain;
using AuthServer.Firmware.Infrastructure.Http;
using AuthServer.Firmware.Infrastructure.Persistence;
using AuthServer.Identity.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Firmware.Features.GetDeviceReleases;

internal sealed class Endpoint(FirmwareDbContext context) : EndpointWithoutRequest<DeviceReleasesResponse>
{
    public override void Configure()
    {
        Get("/device/firmware");
        Policies(IdentityPolicies.Device);
        Options(route => route.WithTags("Device").WithName("GetDeviceReleases"));
        Summary(summary =>
        {
            summary.Summary = "Every firmware release, newest upload first; ?board= one board's (device).";
            summary.Description = "active: the one the status answer offers. Any of them can be installed: GET /api/device/firmware/{id}/image.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var board = Query<string>("board", isRequired: false);

        if (board is not null && !Boards.IsKnown(board))
        {
            await FirmwareProblem.SendAsync(HttpContext, FirmwareErrors.BoardUnknown, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        var releases = await context.Releases
            .Where(release => board == null || release.Board == board)
            .OrderByDescending(release => release.UploadedAt)
            .ThenByDescending(release => release.Id)
            .Select(release => new DeviceRelease(
                release.Id,
                release.Board,
                release.Version,
                release.Built,
                release.Size,
                release.Sha256,
                release.Active,
                release.UploadedAt))
            .ToListAsync(cancellationToken);

        await Send.OkAsync(new DeviceReleasesResponse(releases), cancellationToken);
    }
}

internal sealed record DeviceReleasesResponse(IReadOnlyList<DeviceRelease> Releases);

/// <param name="Version">esp_app_desc_t.version: what the device reports once it runs it.</param>
/// <param name="Sha256">Of the whole image, lower-case hex: check it after the last piece.</param>
internal sealed record DeviceRelease(
    Guid Id,
    string Board,
    string Version,
    string Built,
    long Size,
    string Sha256,
    bool Active,
    DateTimeOffset UploadedAt);
