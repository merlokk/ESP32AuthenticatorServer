using AuthServer.Firmware.Domain;
using AuthServer.Firmware.Infrastructure.Persistence;
using AuthServer.Identity.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Firmware.Features.GetReleases;

internal sealed class Endpoint(FirmwareDbContext context) : EndpointWithoutRequest<ReleasesResponse>
{
    public override void Configure()
    {
        Get("/firmware/releases");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Firmware").WithName("GetReleases"));
        Summary(summary =>
        {
            summary.Summary = "The boards and every uploaded release, newest first (admin).";
            summary.Description = "The active release of a board is offered to its devices in the status answer.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var releases = await context.Releases
            .OrderByDescending(release => release.UploadedAt)
            .ThenByDescending(release => release.Id)
            .Select(release => new ReleaseRow(
                release.Id,
                release.Board,
                release.Version,
                release.Built,
                release.Idf,
                release.Size,
                release.Sha256,
                release.Active,
                release.UploadedAt))
            .ToListAsync(cancellationToken);

        await Send.OkAsync(
            new ReleasesResponse([.. Boards.All.Select(board => new BoardRow(board.Id, board.Name))], releases),
            cancellationToken);
    }
}

internal sealed record ReleasesResponse(IReadOnlyList<BoardRow> Boards, IReadOnlyList<ReleaseRow> Releases);

internal sealed record BoardRow(string Id, string Name);

/// <param name="Version">esp_app_desc_t.version: what a device running it reports.</param>
/// <param name="Built">The image's build date and time.</param>
/// <param name="Sha256">Of the whole image, lower-case hex.</param>
internal sealed record ReleaseRow(
    Guid Id,
    string Board,
    string Version,
    string Built,
    string Idf,
    long Size,
    string Sha256,
    bool Active,
    DateTimeOffset UploadedAt);
