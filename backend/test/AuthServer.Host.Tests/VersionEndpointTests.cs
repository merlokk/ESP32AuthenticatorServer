using System.Net;
using System.Net.Http.Json;
using AuthServer.Host.Startup;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AuthServer.Host.Tests;

public sealed class VersionEndpointTests
{
    [Fact]
    public async Task Version_answers_anonymously_with_the_build()
    {
        // Program is internal, so the factory is a local rather than a class fixture: a public test
        // class cannot take an internal type in its constructor.
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/version");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<VersionResponse>();
        body.ShouldNotBeNull();
        body.Version.ShouldBe(ProductVersion.Value);
        body.Commit.ShouldBe(ProductVersion.Commit);
    }
}
