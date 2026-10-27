using System.Net;
using System.Text.Json.Nodes;

namespace AuthServer.IntegrationTests;

internal static class Problems
{
    public const string Prefix = "urn:authserver:problem:";

    public static async Task ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(status, body);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        JsonNode.Parse(body)!["type"]!.GetValue<string>().ShouldBe(Prefix + code);
    }

    public static async Task<JsonNode> JsonAsync(this HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, body);
        return JsonNode.Parse(body)!;
    }
}
