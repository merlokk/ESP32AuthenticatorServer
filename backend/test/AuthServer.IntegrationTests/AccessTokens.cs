using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Tests.Devices;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace AuthServer.IntegrationTests;

// The browser's side of sign-in: the cookie from a sign-in slice, then authorization code + PKCE.
internal static class AccessTokens
{
    public static HttpClient CreateBrowser(WebApplicationFactory<Program> application) =>
        application.CreateDefaultClient(new CookieHandler());

    public static HttpClient Bearer(WebApplicationFactory<Program> application, string accessToken)
    {
        var client = application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
        return client;
    }

    public static async Task<HttpClient> BearerAsync(WebApplicationFactory<Program> application, TestAccount account) =>
        Bearer(application, (await ObtainAsync(CreateBrowser(application), account)).AccessToken);

    public static Task<HttpResponseMessage> SignInWithTotpAsync(HttpClient browser, string email, string code) =>
        browser.PostAsJsonAsync("/api/identity/sign-in/totp", new { email, code });

    // A code step is accepted once per device, so this walks the three acceptable steps (the
    // previous, the current, the next) until one has not been used yet.
    public static async Task SignInAsync(HttpClient browser, string email, VirtualDevice device)
    {
        var step = Totp.StepAt(DateTimeOffset.UtcNow);
        HttpResponseMessage? last = null;

        for (var offset = -1; offset <= 1; offset++)
        {
            last = await SignInWithTotpAsync(browser, email, Totp.Code(device.TotpSecret, step + offset));

            if (last.StatusCode == HttpStatusCode.OK)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"TOTP sign-in for {email} answered {(int)last!.StatusCode}: {await last.Content.ReadAsStringAsync()}");
    }

    public static async Task<(string AccessToken, string RefreshToken)> ObtainAsync(HttpClient browser, TestAccount account)
    {
        await SignInAsync(browser, account.Email, account.Device);
        return await ObtainFromCookieAsync(browser);
    }

    public static async Task<(string AccessToken, string RefreshToken)> ObtainFromCookieAsync(HttpClient browser)
    {
        var (verifier, challenge) = CreatePkcePair();

        var authorize = await browser.GetAsync(AuthorizeUrl(challenge, state: "test"));

        var location = authorize.Headers.Location
            ?? throw new InvalidOperationException(
                $"/connect/authorize answered {(int)authorize.StatusCode} with no Location: "
                + await authorize.Content.ReadAsStringAsync());

        var code = System.Web.HttpUtility.ParseQueryString(location.Query)["code"]
            ?? throw new InvalidOperationException($"No authorization code in {location}");

        var response = await RedeemAsync(browser, code, verifier);
        var body = await response.Content.ReadAsStringAsync();

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"/connect/token answered {(int)response.StatusCode}: {body}");
        }

        var token = JsonNode.Parse(body)!;
        return (token["access_token"]!.GetValue<string>(), token["refresh_token"]!.GetValue<string>());
    }

    public static Task<HttpResponseMessage> RedeemAsync(HttpClient browser, string code, string verifier) =>
        browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = TestHost.ClientId,
            ["redirect_uri"] = TestHost.RedirectUri,
            ["code"] = code,
            ["code_verifier"] = verifier,
        }));

    public static Task<HttpResponseMessage> RenewAsync(HttpClient client, string refreshToken) =>
        client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = TestHost.ClientId,
            ["refresh_token"] = refreshToken,
        }));

    public static Task<HttpResponseMessage> RevokeAsync(HttpClient client, string refreshToken) =>
        client.PostAsync("/connect/revocation", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = TestHost.ClientId,
            ["token"] = refreshToken,
            ["token_type_hint"] = "refresh_token",
        }));

    public static async Task<string> AccessTokenOf(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["access_token"]!.GetValue<string>();

    public static JsonWebToken Read(string accessToken) => new JsonWebTokenHandler().ReadJsonWebToken(accessToken);

    public static string AuthorizeUrl(string challenge, string state) =>
        "/connect/authorize"
        + $"?client_id={TestHost.ClientId}"
        + "&response_type=code"
        + $"&redirect_uri={Uri.EscapeDataString(TestHost.RedirectUri)}"
        + "&scope=" + Uri.EscapeDataString("openid profile offline_access")
        + $"&state={state}"
        + $"&code_challenge={challenge}"
        + "&code_challenge_method=S256";

    public static (string Verifier, string Challenge) CreatePkcePair()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Keeps cookies like a browser, without following redirects.
    public sealed class CookieHandler : DelegatingHandler
    {
        public CookieContainer Cookies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var header = Cookies.GetCookieHeader(uri);

            if (header.Length > 0)
            {
                request.Headers.Add("Cookie", header);
            }

            var response = await base.SendAsync(request, cancellationToken);

            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var setCookie in setCookies)
                {
                    Cookies.SetCookies(uri, setCookie);
                }
            }

            return response;
        }
    }
}
