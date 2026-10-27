using AuthServer.Shared;
using Microsoft.AspNetCore.Http;

namespace AuthServer.Identity.Infrastructure.Http;

/// <summary>An <see cref="Error"/> as Problem Details; the <c>type</c> is public API.</summary>
internal static class IdentityProblem
{
    public const string TypePrefix = "urn:authserver:problem:";

    public static Task SendAsync(HttpContext context, Error error, int statusCode, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;

        return context.Response.WriteAsJsonAsync(
            new ProblemResponse(TypePrefix + error.Code, error.Message, statusCode, context.Request.Path),
            options: null,
            contentType: "application/problem+json",
            cancellationToken);
    }

    internal sealed record ProblemResponse(string Type, string Title, int Status, string Instance);
}
