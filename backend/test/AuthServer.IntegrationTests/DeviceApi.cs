using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AuthServer.Identity.Tests.Devices;

namespace AuthServer.IntegrationTests;

// The firmware's side: registration and private_key_jwt sign-in.
internal static class DeviceApi
{
    // rfc7523bis: the assertion's audience is the issuer, exactly as discovery names it.
    public const string Issuer = TestHost.PublicOrigin + "/";

    public static Task<HttpResponseMessage> RegisterAsync(HttpClient client, VirtualDevice device, string email) =>
        RegisterAsync(client, device.Registration(email));

    public static Task<HttpResponseMessage> RegisterAsync(HttpClient client, VirtualDevice.RegistrationBody body) =>
        client.PostAsJsonAsync("/api/identity/devices/register", body);

    public static async Task<string> ClientIdOf(HttpResponseMessage registered) =>
        JsonNode.Parse(await registered.Content.ReadAsStringAsync())!["clientId"]!.GetValue<string>();

    public static Task<HttpResponseMessage> TokenAsync(
        HttpClient client, VirtualDevice device, string clientId, string audience = Issuer) =>
        client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ["client_assertion"] = device.ClientAssertion(clientId, audience, DateTimeOffset.UtcNow),
        }));
}
