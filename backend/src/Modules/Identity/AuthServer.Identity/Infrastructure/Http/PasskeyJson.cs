using System.Buffers.Text;
using System.Text.Json;

namespace AuthServer.Identity.Infrastructure.Http;

/// <summary>Reads the credential id out of the browser's PublicKeyCredential JSON.</summary>
internal static class PasskeyJson
{
    public static byte[]? CredentialIdOf(string? credentialJson)
    {
        try
        {
            using var document = JsonDocument.Parse(credentialJson ?? string.Empty);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.GetString() is { } value
                && Base64Url.IsValid(value)
                    ? Base64Url.DecodeFromChars(value)
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
