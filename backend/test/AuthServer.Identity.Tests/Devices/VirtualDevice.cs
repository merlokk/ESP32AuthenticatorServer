using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using AuthServer.Identity.Domain;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AuthServer.Identity.Tests.Devices;

// The firmware's side of the device protocol in software: a P-256 key, a hardware id, a MAC and
// the TOTP secret it hands the server. The signed message is built here by hand from the spec in
// architecture.md §4.6, not by the server's code, so a format change breaks a test.
internal sealed class VirtualDevice : IDisposable
{
    public const string MessagePrefix = "esp32authenticator-device-registration-v1";

    public VirtualDevice(string? hardwareId = null, string? mac = null)
    {
        HardwareId = hardwareId ?? "X4-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
        Mac = mac ?? string.Join(':', RandomNumberGenerator.GetBytes(6).Select(part => part.ToString("X2")));
    }

    public ECDsa Key { get; private set; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public string HardwareId { get; }

    public string Mac { get; }

    public byte[] TotpSecret { get; private set; } = RandomNumberGenerator.GetBytes(20);

    public string TotpSecretBase32 => Base32.Encode(TotpSecret);

    // Uncompressed SEC1 point: 0x04 || X || Y.
    public string PublicKey
    {
        get
        {
            var point = Key.ExportParameters(includePrivateParameters: false).Q;
            return Base64Url.EncodeToString([0x04, .. point.X!, .. point.Y!]);
        }
    }

    // A factory reset: a new key and a new TOTP secret, the same hardware.
    public void Reset()
    {
        Key.Dispose();
        Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        TotpSecret = RandomNumberGenerator.GetBytes(20);
    }

    public static string Message(string email, string hardwareId, string mac, string totpSecret, string publicKey) =>
        string.Join('\n', MessagePrefix, email, hardwareId, mac, totpSecret, publicKey);

    // Raw r || s, as micro-ecc produces it.
    public string Sign(string message) =>
        Base64Url.EncodeToString(Key.SignData(Encoding.UTF8.GetBytes(message), HashAlgorithmName.SHA256));

    public RegistrationBody Registration(string email) => new(
        email,
        HardwareId,
        Mac,
        TotpSecretBase32,
        PublicKey,
        Sign(Message(email, HardwareId, Mac, TotpSecretBase32, PublicKey)));

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
            SigningCredentials = new SigningCredentials(
                new ECDsaSecurityKey(Key), SecurityAlgorithms.EcdsaSha256),
        });

    public void Dispose() => Key.Dispose();

    internal sealed record RegistrationBody(
        string Email,
        string HardwareId,
        string Mac,
        string TotpSecret,
        string PublicKey,
        string Signature);
}
