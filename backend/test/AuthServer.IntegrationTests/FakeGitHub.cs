using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;

namespace AuthServer.IntegrationTests;

/// <summary>
/// The GitHub REST API as the import uses it (the releases of one repository, an asset's metadata
/// and its bytes), served from memory. Tests run one after another (AssemblyInfo.cs), so one set.
/// </summary>
internal sealed class FakeGitHub : HttpMessageHandler
{
    public const string Repository = "owner/firmware";

    public static readonly FakeGitHub Instance = new();

    private List<Release> _releases = [];

    public bool Down { get; set; }

    public void Serve(params Release[] releases)
    {
        _releases = [.. releases];
        Down = false;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var prefix = $"/repos/{Repository}/releases";

        if (Down)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        if (path == prefix)
        {
            return Task.FromResult(Json(_releases.Select(release => new
            {
                tag_name = release.Tag,
                name = release.Tag,
                draft = false,
                prerelease = release.Prerelease,
                published_at = release.PublishedAt,
                assets = release.Assets.Select(Metadata),
            })));
        }

        if (path.StartsWith(prefix + "/assets/", StringComparison.Ordinal)
            && long.TryParse(path[(prefix.Length + "/assets/".Length)..], out var id)
            && _releases.SelectMany(release => release.Assets).FirstOrDefault(asset => asset.Id == id) is { } found)
        {
            return Task.FromResult(request.Headers.Accept.Any(accept => accept.MediaType == "application/octet-stream")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(found.Content) }
                : Json(Metadata(found)));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static object Metadata(Asset asset) => new
    {
        id = asset.Id,
        name = asset.Name,
        size = asset.Content.Length,
        digest = asset.Digest ?? "sha256:" + Convert.ToHexStringLower(SHA256.HashData(asset.Content)),
    };

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    internal sealed record Release(string Tag, Asset[] Assets, bool Prerelease = false)
    {
        public DateTimeOffset PublishedAt { get; init; } = new(2026, 10, 29, 8, 43, 13, TimeSpan.Zero);
    }

    /// <param name="Digest">What GitHub states; null: the true one.</param>
    internal sealed record Asset(long Id, string Name, byte[] Content, string? Digest = null);
}
