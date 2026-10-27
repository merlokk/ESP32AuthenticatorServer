using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AuthServer.Host.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AuthServer.IntegrationTests;

public sealed class HealthEndpointTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/health")]
    public async Task Each_probe_answers_anonymously_with_the_database_reachable(string path)
    {
        var response = await fixture.Application.CreateClient().GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task The_OpenAPI_document_has_the_identity_api_and_no_probe()
    {
        var document = await fixture.Application.CreateClient().GetFromJsonAsync<JsonNode>("/swagger/v1/swagger.json");
        var paths = document!["paths"]!.AsObject().Select(path => path.Key).ToList();

        paths.ShouldContain("/api/identity/me");
        paths.ShouldNotContain(path => path.StartsWith("/health") || path.StartsWith("/connect"));
    }

    [Fact]
    public async Task Readiness_is_unhealthy_when_PostgreSQL_cannot_be_reached()
    {
        var check = new DatabaseHealthCheck(Configured("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x"));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Readiness_is_unhealthy_rather_than_throwing_when_nothing_is_configured()
    {
        var result = await new DatabaseHealthCheck(Configured(null)).CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    private static IConfiguration Configured(string? connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:AuthServer"] = connectionString })
            .Build();
}
