using System.Security.Cryptography;
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

        var file = await ReadBodyAsync(cancellationToken);

        if (file is null)
        {
            await FirmwareProblem.SendAsync(HttpContext, FirmwareErrors.ImageTooLarge, StatusCodes.Status413PayloadTooLarge, cancellationToken);
            return;
        }

        var image = AppImage.Parse(file, settings.Project);

        if (!image.IsSuccess)
        {
            await FirmwareProblem.SendAsync(HttpContext, image.Error, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        var release = new FirmwareRelease
        {
            Id = Guid.CreateVersion7(),
            Board = board!,
            Version = image.Value.Version,
            Built = image.Value.Built,
            Idf = image.Value.Idf,
            Size = file.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(file)),
            UploadedAt = clock.GetUtcNow(),
            UploadedBy = FirmwareProblem.Caller(User),
            Content = file,
        };

        context.Releases.Add(release);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await FirmwareProblem.SendAsync(HttpContext, FirmwareErrors.ReleaseExists, StatusCodes.Status409Conflict, cancellationToken);
            return;
        }

        await Send.OkAsync(
            new UploadReleaseResponse(
                release.Id, release.Board, release.Version, release.Built, release.Idf, release.Size, release.Sha256, release.Active, release.UploadedAt),
            cancellationToken);
    }

    /// <summary>The body, or null past the limit (read no further than one chunk over it).</summary>
    private async Task<byte[]?> ReadBodyAsync(CancellationToken cancellationToken)
    {
        if (HttpContext.Request.ContentLength > settings.MaxImageBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;

        while ((read = await HttpContext.Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);

            if (buffer.Length > settings.MaxImageBytes)
            {
                return null;
            }
        }

        return buffer.ToArray();
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
