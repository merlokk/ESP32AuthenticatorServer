using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuthServer.Shared;

namespace AuthServer.Identity.Domain;

/// <summary>
/// The firmware's registration, verified: a compact JWS (ES256, raw r || s) signed with the key
/// whose public half is its own <c>pub</c> claim, so it proves possession of the key. Whether it
/// may register is the account's registration window, decided elsewhere. architecture.md §4.6.
/// </summary>
internal sealed record DeviceRegistration(string Email, string Serial, string Mac, byte[] TotpSecret, byte[] PublicKey)
{
    /// <summary>How far <c>iat</c> may be from the server's clock, either way.</summary>
    public static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(10);

    public const int SerialLength = 32;

    private const int MinimumSecretLength = 10;

    private const int MaximumSecretLength = 64;

    public static Result<DeviceRegistration> Verify(string? token, DateTimeOffset now)
    {
        var parts = (token ?? string.Empty).Trim().Split('.');

        if (parts.Length != 3
            || Decode(parts[0]) is not { } header
            || Decode(parts[1]) is not { } payload
            || Decode(parts[2]) is not { Length: 64 } signature
            || !IsEs256(header)
            || ClaimsOf(payload) is not { } claims
            || KeyOf(claims.PublicKey) is not { } key)
        {
            return IdentityErrors.RegistrationInvalid;
        }

        using (key)
        {
            var signed = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");

            if (!key.VerifyData(signed, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return IdentityErrors.RegistrationRefused;
            }
        }

        // After the signature: an unsigned iat says nothing.
        if ((now - claims.IssuedAt).Duration() > MaximumClockSkew)
        {
            return IdentityErrors.RegistrationStale;
        }

        return claims.Registration;
    }

    private static bool IsEs256(byte[] header)
    {
        try
        {
            using var document = JsonDocument.Parse(header);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("alg", out var algorithm)
                && algorithm.ValueKind == JsonValueKind.String
                && algorithm.GetString() == "ES256";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Claims? ClaimsOf(byte[] payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || Text(root, "email") is not { } email || !IsEmail(email)
                || Text(root, "serial") is not { } serial || SerialOf(serial) is not { } normalizedSerial
                || Text(root, "mac") is not { } mac || MacOf(mac) is not { } normalizedMac
                || Text(root, "totp") is not { } totp
                || !Base32.TryDecode(totp, out var secret) || secret.Length is < MinimumSecretLength or > MaximumSecretLength
                || Text(root, "pub") is not { } pub || PointOf(pub) is not { } point
                || !root.TryGetProperty("iat", out var iat) || !iat.TryGetInt64(out var issuedAt))
            {
                return null;
            }

            return new Claims(
                new DeviceRegistration(email, normalizedSerial, normalizedMac, secret, point),
                point,
                DateTimeOffset.FromUnixTimeSeconds(issuedAt));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // As the device takes it: ASCII, 3 to 64 characters, one '@'; then a bare address.
    private static bool IsEmail(string value) =>
        value.Length is >= 3 and <= 64
        && value.All(char.IsAscii)
        && value.Count(character => character == '@') == 1
        && AccountFields.EmailOf(value).IsSuccess;

    // The chip's eFuse id: 32 hex characters, stored lower case.
    private static string? SerialOf(string value) =>
        value.Length == SerialLength && value.All(char.IsAsciiHexDigit) ? value.ToLowerInvariant() : null;

    // aa:bb:cc:dd:ee:ff, stored lower case.
    private static string? MacOf(string value)
    {
        var groups = value.Split(':');

        return groups.Length == 6 && groups.All(group => group.Length == 2 && group.All(char.IsAsciiHexDigit))
            ? value.ToLowerInvariant()
            : null;
    }

    // Uncompressed SEC1: 0x04 || X || Y, 65 bytes.
    private static byte[]? PointOf(string value) => Decode(value) is [0x04, ..] and { Length: 65 } point ? point : null;

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
            return value.Length == 0 ? null : Base64Url.DecodeFromChars(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private sealed record Claims(DeviceRegistration Registration, byte[] PublicKey, DateTimeOffset IssuedAt);
}
