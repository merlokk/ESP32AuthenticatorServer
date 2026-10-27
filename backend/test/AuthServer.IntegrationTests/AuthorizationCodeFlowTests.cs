using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AuthServer.Identity.Domain;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AuthServer.IntegrationTests;

// UC-11: authorization code + PKCE for the BFF client.
public sealed class AuthorizationCodeFlowTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    [Fact]
    public async Task Discovery_names_the_public_origin_and_publishes_an_EC_key()
    {
        // As the BFF forwards it: the endpoints are on the public origin.
        var client = fixture.Application.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "localhost:3000");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");

        var document = await client.GetFromJsonAsync<JsonNode>("/.well-known/openid-configuration");

        document!["issuer"]!.GetValue<string>().TrimEnd('/').ShouldBe(TestHost.PublicOrigin);
        document["id_token_signing_alg_values_supported"]!.AsArray()
            .Select(value => value!.GetValue<string>())
            .ShouldContain("ES256");

        foreach (var endpoint in new[] { "authorization_endpoint", "token_endpoint", "end_session_endpoint", "revocation_endpoint" })
        {
            document[endpoint]!.GetValue<string>().ShouldStartWith(TestHost.PublicOrigin);
        }

        var jwks = await client.GetFromJsonAsync<JsonNode>("/.well-known/jwks");
        var key = jwks!["keys"]!.AsArray()[0]!;

        key["kty"]!.GetValue<string>().ShouldBe("EC");
        key["kid"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Authorize_without_a_cookie_redirects_to_the_sign_in_page_on_the_public_origin()
    {
        await fixture.ResetAsync();
        var (_, challenge) = AccessTokens.CreatePkcePair();

        var response = await fixture.CreateBrowser().GetAsync(AccessTokens.AuthorizeUrl(challenge, state: "abc"));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var location = response.Headers.Location!.ToString();
        location.ShouldStartWith(TestHost.PublicOrigin + "/sign-in?returnUrl=");
        location.ShouldContain(Uri.EscapeDataString("code_challenge"));
        location.ShouldContain(Uri.EscapeDataString("state=abc"));
    }

    [Fact]
    public async Task The_flow_returns_a_token_with_the_public_issuer_and_the_users_facts()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("operator@example.com", UserRoles.Admin);
        var browser = fixture.CreateBrowser();
        var (verifier, challenge) = AccessTokens.CreatePkcePair();

        await AccessTokens.SignInAsync(browser, account.Email, account.Device);

        var authorize = await browser.GetAsync(AccessTokens.AuthorizeUrl(challenge, state: "xyz"));

        authorize.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var redirect = authorize.Headers.Location!;
        redirect.ToString().ShouldStartWith(TestHost.RedirectUri);

        var query = System.Web.HttpUtility.ParseQueryString(redirect.Query);
        query["state"].ShouldBe("xyz");

        var redeemed = await AccessTokens.RedeemAsync(browser, query["code"]!, verifier);
        var body = await redeemed.Content.ReadAsStringAsync();
        redeemed.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        var token = JsonNode.Parse(body)!;
        token["refresh_token"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        token["id_token"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();

        var validated = await ValidateAsync(token["access_token"]!.GetValue<string>());

        validated.Issuer.TrimEnd('/').ShouldBe(TestHost.PublicOrigin);
        validated.Audiences.ShouldContain("authserver.api");
        validated.Subject.ShouldBe(account.Id.ToString());
        validated.GetClaim("email").Value.ShouldBe(account.Email);
        validated.GetClaim("name").Value.ShouldNotBeNullOrWhiteSpace();
        validated.Claims.Where(claim => claim.Type == "role").Select(claim => claim.Value).ShouldBe([UserRoles.Admin]);
        validated.Claims.ShouldNotContain(claim => claim.Type == "device_id");
    }

    [Fact]
    public async Task A_code_redeemed_without_its_verifier_is_refused()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("user@example.com");
        var browser = fixture.CreateBrowser();
        var (_, challenge) = AccessTokens.CreatePkcePair();

        await AccessTokens.SignInAsync(browser, account.Email, account.Device);
        var authorize = await browser.GetAsync(AccessTokens.AuthorizeUrl(challenge, state: "s"));
        var code = System.Web.HttpUtility.ParseQueryString(authorize.Headers.Location!.Query)["code"]!;

        var response = await AccessTokens.RedeemAsync(browser, code, AccessTokens.CreatePkcePair().Verifier);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_code_is_single_use()
    {
        await fixture.ResetAsync();
        var account = await fixture.CreateUserAsync("user@example.com");
        var browser = fixture.CreateBrowser();
        var (verifier, challenge) = AccessTokens.CreatePkcePair();

        await AccessTokens.SignInAsync(browser, account.Email, account.Device);
        var authorize = await browser.GetAsync(AccessTokens.AuthorizeUrl(challenge, state: "s"));
        var code = System.Web.HttpUtility.ParseQueryString(authorize.Headers.Location!.Query)["code"]!;

        (await AccessTokens.RedeemAsync(browser, code, verifier)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AccessTokens.RedeemAsync(browser, code, verifier)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<JsonWebToken> ValidateAsync(string accessToken)
    {
        var jwks = await fixture.Application.CreateClient().GetStringAsync("/.well-known/jwks");

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(accessToken, new TokenValidationParameters
        {
            ValidIssuers = [TestHost.PublicOrigin, TestHost.PublicOrigin + "/"],
            ValidAudience = "authserver.api",
            IssuerSigningKeys = new JsonWebKeySet(jwks).GetSigningKeys(),
            ClockSkew = TimeSpan.FromSeconds(5),
        });

        result.IsValid.ShouldBeTrue(result.Exception?.Message);
        return (JsonWebToken)result.SecurityToken;
    }
}
