using AuthServer.Firmware.Domain;
using AuthServer.Firmware.Infrastructure.Http;
using AuthServer.Firmware.Infrastructure.Persistence;
using AuthServer.Identity.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuthServer.Firmware.Features.UploadRelease;

/// <summary>An image as idf.py builds it (build/esp32-authenticator.bin), named by its own descriptor; inactive.</summary>
internal sealed class Endpoint(FirmwareDbContext context, FirmwareSettings settings, TimeProvider clock) : EndpointWithoutRequest<UploadReleaseResponse>
{
    public override void Configure()
    {
        Post("/firmware/releases");
        Policies(IdentityPolicies.Admin);
        Options(route => route
            .WithTags("Firmware")
            .WithName("UploadRelease")
            .Accepts<byte[]>("application/octet-stream"));
        Summary(summary =>
        {
            summary.Summary = "Upload a firmware image for a board (admin).";
            summary.Description =
                "?board= a board id; the body the app image as is. The version comes from the image. Kept inactive: "
                + "activate it to offer it. firmware.image-invalid: not this firmware's ESP32-S3 image, or broken.";
            summary.Responses[200] = "Kept, inactive.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var board = Query<string>("board", isRequired: false);

        if (!Boards.IsKnown(board))
        {
            await FirmwareProblem.SendAsync(HttpContext, FirmwareErrors.BoardUnknown, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        var file = await BoundedBody.ReadAsync(HttpContext.Request.Body, HttpContext.Request.ContentLength, settings.MaxImageBytes, cancellationToken);

        if (file is null)
        {
            await FirmwareProblem.SendAsync(HttpContext, FirmwareErrors.ImageTooLarge, StatusCodes.Status413PayloadTooLarge, cancellationToken);
            return;
        }

        var release = FirmwareRelease.Of(board!, file, settings.Project, clock.GetUtcNow(), FirmwareProblem.Caller(User));

        if (!release.IsSuccess)
        {
            await FirmwareProblem.SendAsync(HttpContext, release.Error, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        context.Releases.Add(release.Value);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await FirmwareProblem.SendAsync(HttpContext, FirmwareErrors.ReleaseExists, StatusCodes.Status409Conflict, cancellationToken);
            return;
        }

        var kept = release.Value;

        await Send.OkAsync(
            new UploadReleaseResponse(kept.Id, kept.Board, kept.Version, kept.Built, kept.Idf, kept.Size, kept.Sha256, kept.Active, kept.UploadedAt),
            cancellationToken);
    }
}

internal sealed record UploadReleaseResponse(
    Guid Id,
    string Board,
    string Version,
    string Built,
    string Idf,
    long Size,
    string Sha256,
    bool Active,
    DateTimeOffset UploadedAt);
