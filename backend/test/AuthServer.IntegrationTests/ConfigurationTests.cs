using System.Net;
using AuthServer.Identity.Domain;

namespace AuthServer.IntegrationTests;

// UC-22: an admin reads the configuration the server runs with; secrets only as set or not.
public sealed class ConfigurationTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task An_admin_reads_the_running_configuration()
    {
        await fixture.ResetAsync();
        var admin = await AccessTokens.BearerAsync(fixture.Application, await fixture.CreateUserAsync("admin@example.com", UserRoles.Admin));

        var response = await admin.GetAsync("/api/identity/configuration");
        var body = await response.Content.ReadAsStringAsync();
        var configuration = await response.JsonAsync();

        configuration["publicOrigin"]!.GetValue<string>().ShouldBe(TestHost.PublicOrigin);
        configuration["clientId"]!.GetValue<string>().ShouldBe(TestHost.ClientId);
        configuration["accessTokenLifetimeSeconds"]!.GetValue<long>().ShouldBe(2);
        configuration["refreshTokenLifetimeSeconds"]!.GetValue<long>().ShouldBe(3600);
        configuration["inviteWindowSeconds"]!.GetValue<long>().ShouldBe(86400);
        configuration["deviceWindowSeconds"]!.GetValue<long>().ShouldBe(3600);
        configuration["totpEncryptionKeySet"]!.GetValue<bool>().ShouldBeTrue();
        configuration["latestFirmware"]!.GetValue<string>().ShouldBe(TestHost.LatestFirmware);
        body.ShouldNotContain(TestHost.TotpEncryptionKey);
    }

    [Fact]
    public async Task Only_an_admin_reads_it()
    {
        await fixture.ResetAsync();
        var user = await AccessTokens.BearerAsync(fixture.Application, await fixture.CreateUserAsync("user@example.com"));

        (await user.GetAsync("/api/identity/configuration")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.Application.CreateClient().GetAsync("/api/identity/configuration")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
