using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuthServer.Identity.Domain;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AuthServer.Identity.Tests.Devices;

// The firmware's side of the device protocol in software: a P-256 key, the chip serial, a MAC and
// the TOTP secret it hands the server. The registration JWS is built here by hand from the
// firmware's spec (architecture.md §4.6), not by any JWT library, so a format change breaks a test.
internal sealed class VirtualDevice : IDisposable
{
    public VirtualDevice(string? serial = null, string? mac = null)
    {
        Serial = serial ?? Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        Mac = mac ?? string.Join(':', RandomNumberGenerator.GetBytes(6).Select(part => part.ToString("x2")));
    }

    public ECDsa Key { get; private set; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>The chip's eFuse id: 32 lower-case hex characters.</summary>
    public string Serial { get; }

    public string Mac { get; }

    public byte[] TotpSecret { get; private set; } = RandomNumberGenerator.GetBytes(20);

    public string TotpSecretBase32 => Base32.Encode(TotpSecret);

    // Uncompressed SEC1 point: 0x04 || X || Y, base64url without padding (87 characters).
    public string PublicKey
    {
        get
        {
            var point = Key.ExportParameters(includePrivateParameters: false).Q;
            return Base64Url.EncodeToString([0x04, .. point.X!, .. point.Y!]);
        }
    }

    // What the firmware does before every registration attempt: a new key and a new TOTP secret.
    public void Reset()
    {
        Key.Dispose();
        Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TotpSecret = RandomNumberGenerator.GetBytes(20);
    }

    public Dictionary<string, object> Claims(string email, DateTimeOffset? issuedAt = null) => new()
    {
        ["email"] = email,
        ["serial"] = Serial,
        ["mac"] = Mac,
        ["totp"] = TotpSecretBase32,
        ["pub"] = PublicKey,
        ["iat"] = (issuedAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(),
    };

    public string Registration(string email, DateTimeOffset? issuedAt = null) => Token(Claims(email, issuedAt));

    // Compact JWS: base64url(header).base64url(payload).base64url(r || s), ES256.
    public string Token(IDictionary<string, object> claims, string algorithm = "ES256", ECDsa? signWith = null)
    {
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["alg"] = algorithm, ["typ"] = "JWT" }));
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(claims));
        var signature = (signWith ?? Key).SignData(Encoding.ASCII.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256);

        return $"{header}.{payload}.{Encode(signature)}";
    }

    public string Code(DateTimeOffset at) => Totp.Code(TotpSecret, Totp.StepAt(at));

    // RFC 7523 client authentication: a short JWT signed with the device key. OpenIddict 7 follows
    // rfc7523bis and wants the explicit type header.
    public string ClientAssertion(string clientId, string audience, DateTimeOffset now) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            TokenType = "client-authentication+jwt",
            Issuer = clientId,
            Audience = audience,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = clientId,
                ["jti"] = Guid.NewGuid().ToString("N"),
            },
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddMinutes(2).UtcDateTime,
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(Key), SecurityAlgorithms.EcdsaSha256),
        });

    public void Dispose() => Key.Dispose();

    private static string Encode(byte[] bytes) => Base64Url.EncodeToString(bytes);
}
