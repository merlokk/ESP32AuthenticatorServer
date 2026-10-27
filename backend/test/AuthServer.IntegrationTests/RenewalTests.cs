using System.Net;
using System.Text.Json.Nodes;

namespace AuthServer.IntegrationTests;

// UC-11: refresh tokens are not rolling, so concurrent renewals in the BFF never revoke each other;
// sign-out ends the refresh token.
public sealed class RenewalTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private static readonly TimeSpan PastTheAccessTokenLifetime = TimeSpan.FromSeconds(8);

    [Fact]
    public async Task Ten_concurrent_renewals_all_succeed_and_none_invalidates_another()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("renewal@example.com");
        var browser = fixture.CreateBrowser();
        var (accessToken, refreshToken) = await AccessTokens.ObtainAsync(browser, account);

        (await ReadProfileAsync(accessToken)).ShouldBe(HttpStatusCode.OK);
        await Task.Delay(PastTheAccessTokenLifetime);
        (await ReadProfileAsync(accessToken)).ShouldBe(HttpStatusCode.Unauthorized);

        var renewals = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => AccessTokens.RenewAsync(browser, refreshToken)));

        var renewed = new List<string>();

        foreach (var renewal in renewals)
        {
            var body = await renewal.Content.ReadAsStringAsync();
            renewal.StatusCode.ShouldBe(HttpStatusCode.OK, body);
            renewed.Add(JsonNode.Parse(body)!["access_token"]!.GetValue<string>());
        }

        renewed.Distinct().Count().ShouldBe(10);

        foreach (var token in renewed)
        {
            (await ReadProfileAsync(token)).ShouldBe(HttpStatusCode.OK);
        }

        (await AccessTokens.RenewAsync(browser, refreshToken)).StatusCode.ShouldBe(HttpStatusCode.OK, "rotation must be off");
    }

    [Fact]
    public async Task Signing_out_stops_the_refresh_token_redeeming()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("signout@example.com");
        var browser = fixture.CreateBrowser();
        var (_, refreshToken) = await AccessTokens.ObtainAsync(browser, account);

        (await AccessTokens.RenewAsync(browser, refreshToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AccessTokens.RevokeAsync(browser, refreshToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var endSession = await browser.GetAsync("/connect/endsession");
        ((int)endSession.StatusCode).ShouldBeLessThan(400, await endSession.Content.ReadAsStringAsync());

        (await AccessTokens.RenewAsync(browser, refreshToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The identity cookie is gone too: authorize asks for a sign-in again.
        var (_, challenge) = AccessTokens.CreatePkcePair();
        var authorize = await browser.GetAsync(AccessTokens.AuthorizeUrl(challenge, "s"));
        authorize.Headers.Location!.ToString().ShouldStartWith(TestHost.PublicOrigin + "/sign-in");
    }

    [Fact]
    public async Task A_sign_out_with_no_identity_cookie_answers()
    {
        var response = await fixture.Application.CreateClient().GetAsync("/connect/endsession");

        ((int)response.StatusCode).ShouldBeLessThan(400);
    }

    private async Task<HttpStatusCode> ReadProfileAsync(string accessToken) =>
        (await AccessTokens.Bearer(fixture.Application, accessToken).GetAsync("/api/identity/me")).StatusCode;
}
