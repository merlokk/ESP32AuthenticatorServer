using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AuthServer.Shared;

namespace AuthServer.Identity.Domain;

/// <summary>
/// What a device sends to register, verified: the device signs the fields with the private key
/// whose public half it sends (proof of possession). Whether it may register is the account's
/// registration window, decided elsewhere. Format: architecture.md §4.6.
/// </summary>
internal sealed record DeviceRegistration(string Email, string HardwareId, string Mac, byte[] TotpSecret, byte[] PublicKey)
{
    public const string MessagePrefix = "esp32authenticator-device-registration-v1";

    public const int MaximumHardwareIdLength = 64;

    private const int MinimumSecretLength = 10;

    private const int MaximumSecretLength = 64;

    /// <summary>The signed bytes: the fields exactly as sent, one per line, after the prefix.</summary>
    public static byte[] Message(string email, string hardwareId, string mac, string totpSecret, string publicKey) =>
        Encoding.UTF8.GetBytes(string.Join('\n', MessagePrefix, email, hardwareId, mac, totpSecret, publicKey));

    public static Result<DeviceRegistration> Verify(
        string? email, string? hardwareId, string? mac, string? totpSecret, string? publicKey, string? signature)
    {
        if (email is null || hardwareId is null || mac is null || totpSecret is null || publicKey is null || signature is null
            || AccountFields.EmailOf(email) is not { IsSuccess: true } parsedEmail
            || !IsHardwareId(hardwareId)
            || MacOf(mac) is not { } normalizedMac
            || !Base32.TryDecode(totpSecret, out var secret) || secret.Length is < MinimumSecretLength or > MaximumSecretLength
            || PointOf(publicKey) is not { } point
            || Decode(signature) is not { } signatureBytes)
        {
            return IdentityErrors.RegistrationInvalid;
        }

        using var key = KeyOf(point);

        if (key is null)
        {
            return IdentityErrors.RegistrationInvalid;
        }

        var message = Message(email, hardwareId, mac, totpSecret, publicKey);

        bool verified;

        if (signatureBytes.Length == 64)
        {
            verified = key.VerifyData(message, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        else if (signatureBytes is [0x30, ..] && signatureBytes.Length is >= 8 and <= 72)
        {
            verified = key.VerifyData(message, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        else
        {
            return IdentityErrors.RegistrationInvalid;
        }

        return verified
            ? new DeviceRegistration(parsedEmail.Value, hardwareId, normalizedMac, secret, point)
            : IdentityErrors.RegistrationRefused;
    }

    // Printable ASCII without spaces, so it fits on one line of the message.
    private static bool IsHardwareId(string value) =>
        value.Length is > 0 and <= MaximumHardwareIdLength && value.All(character => character is > ' ' and <= '~');

    // AA:BB:CC:DD:EE:FF from colons, hyphens or bare hex.
    private static string? MacOf(string value)
    {
        var hex = value.Replace(":", string.Empty).Replace("-", string.Empty);

        if (hex.Length != 12 || !hex.All(char.IsAsciiHexDigit) || (value.Length != 12 && value.Length != 17))
        {
            return null;
        }

        return string.Join(':', Enumerable.Range(0, 6).Select(index => hex.Substring(index * 2, 2).ToUpper(CultureInfo.InvariantCulture)));
    }

    // 65 bytes 0x04 || X || Y, or 64 bytes X || Y as micro-ecc exports it.
    private static byte[]? PointOf(string value) => Decode(value) switch
    {
        [0x04, ..] and { Length: 65 } point => point,
        { Length: 64 } raw => [0x04, .. raw],
        _ => null,
    };

    private static ECDsa? KeyOf(byte[] point)
    {
        var key = ECDsa.Create();

        try
        {
            key.ImportParameters(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = point[1..33], Y = point[33..] },
            });

            return key;
        }
        catch (Exception exception) when (exception is CryptographicException or PlatformNotSupportedException)
        {
            // Not a point on the curve (Windows CNG says "not supported").
            key.Dispose();
            return null;
        }
    }

    private static byte[]? Decode(string value)
    {
        try
        {
            return Base64Url.DecodeFromChars(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
