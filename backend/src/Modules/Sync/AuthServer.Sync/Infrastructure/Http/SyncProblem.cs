using AuthServer.Shared;
using Microsoft.AspNetCore.Http;

namespace AuthServer.Sync.Infrastructure.Http;

/// <summary>An <see cref="Error"/> as Problem Details; the <c>type</c> is public API, as in Identity.</summary>
internal static class SyncProblem
{
    public const string TypePrefix = "urn:authserver:problem:";

    public static Task SendAsync(
        HttpContext context,
        Error error,
        int statusCode,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["type"] = TypePrefix + error.Code,
            ["title"] = error.Message,
            ["status"] = statusCode,
            ["instance"] = context.Request.Path.Value,
        };

        foreach (var (name, value) in extensions ?? new Dictionary<string, object?>())
        {
            body[name] = value;
        }

        context.Response.StatusCode = statusCode;

        return context.Response.WriteAsJsonAsync(body, options: null, contentType: "application/problem+json", cancellationToken);
    }
}
