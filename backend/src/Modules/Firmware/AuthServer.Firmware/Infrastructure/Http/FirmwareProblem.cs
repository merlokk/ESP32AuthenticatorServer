using System.Security.Claims;
using AuthServer.Identity.Contracts;
using AuthServer.Shared;
using Microsoft.AspNetCore.Http;

namespace AuthServer.Firmware.Infrastructure.Http;

/// <summary>An <see cref="Error"/> as Problem Details; the <c>type</c> is public API, as in Identity.</summary>
internal static class FirmwareProblem
{
    public const string TypePrefix = "urn:authserver:problem:";

    public static Task SendAsync(HttpContext context, Error error, int statusCode, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;

        return context.Response.WriteAsJsonAsync(
            new Dictionary<string, object?>
            {
                ["type"] = TypePrefix + error.Code,
                ["title"] = error.Message,
                ["status"] = statusCode,
                ["instance"] = context.Request.Path.Value,
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken);
    }

    /// <summary>The caller, the token's sub.</summary>
    public static Guid Caller(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirst(IdentityClaims.Subject)?.Value
            ?? throw new InvalidOperationException("A token without sub passed the policy."));
}
