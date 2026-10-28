using System.Globalization;
using AuthServer.Firmware.Domain;
using AuthServer.Firmware.Infrastructure.Http;
using AuthServer.Firmware.Infrastructure.Persistence;
using AuthServer.Identity.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Firmware.Features.DownloadImage;

/// <summary>
/// The image, whole or one Range at a time: a device writes each piece into its OTA slot as it
/// comes, so a dropped link costs one piece. Only the slice is read from the database (the column
/// is stored uncompressed).
/// </summary>
internal sealed class Endpoint(FirmwareDbContext context) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/device/firmware/{id}/image");
        Policies(IdentityPolicies.Device);
        Options(route => route
            .WithTags("Device")
            .WithName("DownloadImage")
            .Produces(200, typeof(byte[]), "application/octet-stream")
            .Produces(206, typeof(byte[]), "application/octet-stream"));
        Summary(summary =>
        {
            summary.Summary = "A firmware image, whole or in pieces (device).";
            summary.Description =
                "Range: bytes=first-last for a piece (206, Content-Range); past the end 416. ETag is the SHA-256: "
                + "check it after the last piece, before switching the boot slot.";
            summary.Responses[200] = "The whole image.";
            summary.Responses[206] = "The piece asked for.";
            summary.Responses[416] = "The range starts past the end.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        // Not a GUID names no release: 404 like a deleted one, not FastEndpoints' validation answer.
        var id = Route<Guid>("id", isRequired: false);

        var release = id == Guid.Empty
            ? null
            : await context.Releases
                .Where(entity => entity.Id == id)
                .Select(entity => new { entity.Size, entity.Sha256 })
                .SingleOrDefaultAsync(cancellationToken);

        if (release is null)
        {
            await FirmwareProblem.SendAsync(HttpContext, FirmwareErrors.ReleaseNotFound, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        var response = HttpContext.Response;
        response.Headers.AcceptRanges = "bytes";
        response.Headers.ETag = $"\"{release.Sha256}\"";
        response.Headers.CacheControl = "private, no-cache";

        var range = ByteRange.Parse(HttpContext.Request.Headers.Range.ToString(), release.Size);

        if (range.Kind == ByteRange.Kind.Unsatisfiable)
        {
            response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
            response.Headers.ContentRange = string.Create(CultureInfo.InvariantCulture, $"bytes */{release.Size}");
            // Sent, or FastEndpoints answers a handler that wrote nothing with 204.
            await response.StartAsync(cancellationToken);
            return;
        }

        // substring(bytea) takes int and counts from 1: an image is far below 2 GB (Firmware:MaxImageBytes).
        // SqlQuery of a scalar reads the column named Value.
        var from = (int)range.Offset + 1;
        var length = (int)range.Length;
        var bytes = await context.Database
            .SqlQuery<byte[]>($"select substring(content from {from} for {length}) as \"Value\" from firmware.releases where id = {id}")
            .SingleAsync(cancellationToken);

        if (range.Kind == ByteRange.Kind.Partial)
        {
            response.StatusCode = StatusCodes.Status206PartialContent;
            response.Headers.ContentRange = string.Create(
                CultureInfo.InvariantCulture, $"bytes {range.Offset}-{range.Offset + bytes.Length - 1}/{release.Size}");
        }

        response.ContentType = "application/octet-stream";
        response.ContentLength = bytes.Length;
        await response.Body.WriteAsync(bytes, cancellationToken);
    }
}
