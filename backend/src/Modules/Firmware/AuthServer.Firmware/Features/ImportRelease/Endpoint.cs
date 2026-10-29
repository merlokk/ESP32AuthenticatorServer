using AuthServer.Firmware.Domain;
using AuthServer.Firmware.Infrastructure.GitHub;
using AuthServer.Firmware.Infrastructure.Http;
using AuthServer.Firmware.Infrastructure.Persistence;
using AuthServer.Identity.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuthServer.Firmware.Features.ImportRelease;

/// <summary>A release image of the configured GitHub repository, fetched by the server and kept as an upload is.</summary>
internal sealed class Endpoint(FirmwareDbContext context, GitHubReleases gitHub, FirmwareSettings settings, TimeProvider clock)
    : Endpoint<ImportReleaseRequest, ImportReleaseResponse>
{
    public override void Configure()
    {
        Post("/firmware/releases/import");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Firmware").WithName("ImportRelease"));
        Summary(summary =>
        {
            summary.Summary = "Import a GitHub release image for a board (admin).";
            summary.Description =
                "assetId from GET /api/firmware/github. Checked as an upload is, and against GitHub's size and SHA-256; "
                + "kept inactive. firmware.asset-not-found, firmware.import-off, firmware.github-unavailable (502).";
            summary.Responses[200] = "Kept, inactive.";
        });
    }

    public override async Task HandleAsync(ImportReleaseRequest request, CancellationToken cancellationToken)
    {
        if (settings.GitHubRepository.Length == 0)
        {
            await FirmwareProblem.SendAsync(HttpContext, FirmwareErrors.ImportOff, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        if (!Boards.IsKnown(request.Board))
        {
            await FirmwareProblem.SendAsync(HttpContext, FirmwareErrors.BoardUnknown, StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }

        var file = await gitHub.DownloadAsync(request.AssetId, settings.MaxImageBytes, cancellationToken);
        var release = file.IsSuccess
            ? FirmwareRelease.Of(request.Board!, file.Value, settings.Project, clock.GetUtcNow(), FirmwareProblem.Caller(User))
            : file.Error;

        if (!release.IsSuccess)
        {
            var status = release.Error == FirmwareErrors.ImageTooLarge ? StatusCodes.Status413PayloadTooLarge
                : release.Error == FirmwareErrors.AssetNotFound ? StatusCodes.Status404NotFound
                : release.Error == FirmwareErrors.GitHubUnavailable ? StatusCodes.Status502BadGateway
                : StatusCodes.Status400BadRequest;
            await FirmwareProblem.SendAsync(HttpContext, release.Error, status, cancellationToken);
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
            new ImportReleaseResponse(kept.Id, kept.Board, kept.Version, kept.Built, kept.Idf, kept.Size, kept.Sha256, kept.Active, kept.UploadedAt),
            cancellationToken);
    }
}

internal sealed record ImportReleaseRequest(string? Board, long AssetId);

internal sealed record ImportReleaseResponse(
    Guid Id,
    string Board,
    string Version,
    string Built,
    string Idf,
    long Size,
    string Sha256,
    bool Active,
    DateTimeOffset UploadedAt);
