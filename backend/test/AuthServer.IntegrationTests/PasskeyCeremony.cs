using System.Net.Http.Json;
using System.Text.Json;
using AuthServer.Identity.Tests.Passkeys;

namespace AuthServer.IntegrationTests;

// The browser's two calls around navigator.credentials.create() and .get().
internal static class PasskeyCeremony
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<HttpResponseMessage> RegisterAsync(
        HttpClient client, VirtualAuthenticator authenticator, string name = "the key")
    {
        var options = await client.PostAsJsonAsync("/api/identity/me/passkeys/options", new { name });

        if (!options.IsSuccessStatusCode)
        {
            return options;
        }

        var creation = (await options.Content.ReadFromJsonAsync<CreationOptionsBody>(Json))!;

        return await client.PostAsJsonAsync("/api/identity/me/passkeys", new
        {
            credentialJson = authenticator.Register(creation.CreationOptions, TestHost.PublicOrigin),
            name,
        });
    }

    public static async Task<HttpResponseMessage> SignInAsync(HttpClient browser, VirtualAuthenticator authenticator)
    {
        var options = await browser.PostAsync("/api/identity/sign-in/passkey/options", content: null);
        options.EnsureSuccessStatusCode();

        var request = (await options.Content.ReadFromJsonAsync<RequestOptionsBody>(Json))!;

        return await browser.PostAsJsonAsync("/api/identity/sign-in/passkey", new
        {
            credentialJson = authenticator.Assert(request.RequestOptions, TestHost.PublicOrigin),
        });
    }

    public static async Task<IReadOnlyList<PasskeyRow>> ListAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/identity/me/passkeys");
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PasskeyList>(Json))!.Passkeys;
    }

    public static Task<HttpResponseMessage> RemoveAsync(HttpClient client, string passkeyId) =>
        client.DeleteAsync($"/api/identity/me/passkeys/{Uri.EscapeDataString(passkeyId)}");

    internal sealed record CreationOptionsBody(string CreationOptions);

    internal sealed record RequestOptionsBody(string RequestOptions);

    internal sealed record PasskeyList(IReadOnlyList<PasskeyRow> Passkeys);

    internal sealed record PasskeyRow(string Id, string Name, DateTimeOffset AddedAt, DateTimeOffset? LastUsedAt);
}
