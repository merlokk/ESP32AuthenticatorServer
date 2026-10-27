using System.Net;
using System.Net.Http.Json;
using AuthServer.Host.Startup;
using AuthServer.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace AuthServer.Host.Tests;

public sealed class VersionEndpointTests
{
    [Fact]
    public async Task Version_answers_anonymously_with_the_build()
    {
        // Program is internal, so the factory is a local rather than a class fixture: a public test
        // class cannot take an internal type in its constructor.
        // No database here: the modules' start-up work (seeding) is left out; settings are real.
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("Identity:PublicOrigin", "http://localhost:3000")
            .UseSetting("Identity:TotpEncryptionKey", Convert.ToBase64String(new byte[32]))
            .ConfigureTestServices(services =>
            {
                foreach (var startup in services.Where(service => service.ServiceType == typeof(IHostedService)
                             && service.ImplementationType?.Assembly == typeof(IdentityModule).Assembly).ToList())
                {
                    services.Remove(startup);
                }
            }));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/version");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<VersionResponse>();
        body.ShouldNotBeNull();
        body.Version.ShouldBe(ProductVersion.Value);
        body.Commit.ShouldBe(ProductVersion.Commit);
    }
}
