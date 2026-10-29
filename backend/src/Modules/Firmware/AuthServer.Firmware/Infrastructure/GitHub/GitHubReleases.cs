using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using AuthServer.Firmware.Domain;
using AuthServer.Firmware.Infrastructure.Http;
using AuthServer.Shared;

namespace AuthServer.Firmware.Infrastructure.GitHub;

/// <summary>
/// The releases of <see cref="FirmwareSettings.GitHubRepository"/> through the GitHub REST API, anonymously:
/// a release lists its assets; an asset's bytes come from its API URL (a redirect to GitHub's storage).
/// </summary>
internal sealed class GitHubReleases(HttpClient client, FirmwareSettings settings)
{
    private string Releases => $"repos/{settings.GitHubRepository}/releases";

    /// <summary>The newest releases, drafts left out (an anonymous caller does not see them anyway).</summary>
    public async Task<Result<IReadOnlyList<GitHubRelease>>> ListAsync(CancellationToken cancellationToken)
    {
        var releases = await GetJsonAsync<List<GitHubRelease>>($"{Releases}?per_page=30", FirmwareErrors.GitHubUnavailable, cancellationToken);

        return releases.IsSuccess
            ? releases.Value.Where(release => !release.Draft).ToList()
            : releases.Error;
    }

    /// <summary>
    /// An asset's bytes, checked against the size and the SHA-256 digest GitHub states for it;
    /// <see cref="FirmwareErrors.ImageTooLarge"/> past <paramref name="maxBytes"/>.
    /// </summary>
    public async Task<Result<byte[]>> DownloadAsync(long assetId, int maxBytes, CancellationToken cancellationToken)
    {
        var asset = await GetJsonAsync<GitHubAsset>($"{Releases}/assets/{assetId}", FirmwareErrors.AssetNotFound, cancellationToken);

        if (!asset.IsSuccess)
        {
            return asset.Error;
        }

        if (asset.Value.Size > maxBytes)
        {
            return FirmwareErrors.ImageTooLarge;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Releases}/assets/{assetId}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return FirmwareErrors.GitHubUnavailable;
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            var file = await BoundedBody.ReadAsync(body, response.Content.Headers.ContentLength, maxBytes, cancellationToken);

            if (file is null)
            {
                return FirmwareErrors.ImageTooLarge;
            }

            // GitHub's digest, when it states one, is what the release was published with.
            var digest = asset.Value.Digest;

            if (file.Length != asset.Value.Size
                || (digest is not null && digest != "sha256:" + Convert.ToHexStringLower(SHA256.HashData(file))))
            {
                return FirmwareErrors.ImageInvalid;
            }

            return file;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return FirmwareErrors.GitHubUnavailable;
        }
    }

    /// <param name="notFound">What a 404 means here.</param>
    private async Task<Result<T>> GetJsonAsync<T>(string path, Error notFound, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return notFound;
            }

            if (!response.IsSuccessStatusCode || await response.Content.ReadFromJsonAsync<T>(cancellationToken) is not { } value)
            {
                return FirmwareErrors.GitHubUnavailable;
            }

            return value;
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException
                                              or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return FirmwareErrors.GitHubUnavailable;
        }
    }
}

internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string Tag,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset> Assets);

/// <param name="Digest">"sha256:&lt;hex&gt;"; absent on assets older than June 2025.</param>
internal sealed record GitHubAsset(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("digest")] string? Digest);
