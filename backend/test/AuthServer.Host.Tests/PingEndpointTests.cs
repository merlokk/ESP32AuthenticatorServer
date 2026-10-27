using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AuthServer.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace AuthServer.Host.Tests;

// UC-40: a device checks it reaches the server, with no token and no database behind the answer.
public sealed class PingEndpointTests
{
    [Fact]
    public async Task Ping_answers_anonymously_with_the_server_time()
    {
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
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var response = await client.GetAsync("/api/device/ping");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl?.NoStore.ShouldBe(true);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        body!["status"]!.GetValue<string>().ShouldBe("ok");
        body["time"]!.GetValue<long>().ShouldBeInRange(before - 1, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1);
    }
}
