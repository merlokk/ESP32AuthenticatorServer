using AuthServer.Firmware.Domain;
using AuthServer.Firmware.Infrastructure.GitHub;
using AuthServer.Firmware.Infrastructure.Http;
using AuthServer.Identity.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AuthServer.Firmware.Features.GetGitHubReleases;

internal sealed class Endpoint(GitHubReleases gitHub, FirmwareSettings settings) : EndpointWithoutRequest<GitHubReleasesResponse>
{
    public override void Configure()
    {
        Get("/firmware/github");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Firmware").WithName("GetGitHubReleases"));
        Summary(summary =>
        {
            summary.Summary = "The newest releases of Firmware:GitHubRepository with their .bin files (admin).";
            summary.Description =
                "An asset's id goes to POST /api/firmware/releases/import. firmware.import-off: no repository configured; "
                + "firmware.github-unavailable (502): GitHub did not answer.";
        });
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        if (settings.GitHubRepository.Length == 0)
        {
            await FirmwareProblem.SendAsync(HttpContext, FirmwareErrors.ImportOff, StatusCodes.Status404NotFound, cancellationToken);
            return;
        }

        var releases = await gitHub.ListAsync(cancellationToken);

        if (!releases.IsSuccess)
        {
            await FirmwareProblem.SendAsync(HttpContext, releases.Error, StatusCodes.Status502BadGateway, cancellationToken);
            return;
        }

        await Send.OkAsync(
            new GitHubReleasesResponse(
                settings.GitHubRepository,
                [
                    .. releases.Value.Select(release => new GitHubReleaseRow(
                        release.Tag,
                        string.IsNullOrWhiteSpace(release.Name) ? release.Tag : release.Name,
                        release.Prerelease,
                        release.PublishedAt,
                        [
                            .. release.Assets
                                .Where(asset => asset.Name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                                .Select(asset => new GitHubAssetRow(asset.Id, asset.Name, asset.Size)),
                        ])),
                ]),
            cancellationToken);
    }
}

internal sealed record GitHubReleasesResponse(string Repository, IReadOnlyList<GitHubReleaseRow> Releases);

/// <param name="Tag">The git tag, "0.6.0".</param>
internal sealed record GitHubReleaseRow(string Tag, string Name, bool Prerelease, DateTimeOffset? PublishedAt, IReadOnlyList<GitHubAssetRow> Assets);

/// <param name="Id">GitHub's asset id: what an import names.</param>
internal sealed record GitHubAssetRow(long Id, string Name, long Size);
