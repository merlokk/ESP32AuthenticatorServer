using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AuthServer.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AuthServer.IntegrationTests;

// UC-11: the access-token signing pair is per process; refresh tokens survive a restart because
// they are Data Protection tokens and the key ring is in the database.
public sealed class EphemeralSigningKeyTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task A_second_process_publishes_another_key_refuses_the_old_access_token_and_renews()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("restart@example.com");
        var (accessToken, refreshToken) = await AccessTokens.ObtainAsync(fixture.CreateBrowser(), account);

        await using var restarted = fixture.CreateSecondApplication();

        var before = await KeyIdAsync(fixture.Application);
        var after = await KeyIdAsync(restarted);
        after.ShouldNotBe(before);

        (await ReadProfileAsync(fixture.Application, accessToken)).ShouldBe(HttpStatusCode.OK);
        (await ReadProfileAsync(restarted, accessToken)).ShouldBe(HttpStatusCode.Unauthorized);

        var renewal = await AccessTokens.RenewAsync(fixture.CreateBrowser(restarted), refreshToken);
        renewal.StatusCode.ShouldBe(HttpStatusCode.OK, await renewal.Content.ReadAsStringAsync());

        var renewed = await AccessTokens.AccessTokenOf(renewal);
        (await ReadProfileAsync(restarted, renewed)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public void No_setting_holds_a_signing_key()
    {
        typeof(IdentitySettings).GetProperties()
            .Select(property => property.Name)
            .ShouldNotContain(name => name.Contains("Signing", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string?> KeyIdAsync(WebApplicationFactory<Program> application)
    {
        var jwks = await application.CreateClient().GetFromJsonAsync<JsonNode>("/.well-known/jwks");
        return jwks!["keys"]!.AsArray()[0]!["kid"]?.GetValue<string>();
    }

    private static async Task<HttpStatusCode> ReadProfileAsync(WebApplicationFactory<Program> application, string accessToken) =>
        (await AccessTokens.Bearer(application, accessToken).GetAsync("/api/identity/me")).StatusCode;
}
