using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AuthServer.Identity.Domain;

/// <summary>RFC 6238 TOTP: HMAC-SHA1, 30-second steps, 6 digits, as the device shows it.</summary>
internal static class Totp
{
    public const int Digits = 6;

    public static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

    public static long StepAt(DateTimeOffset instant) => instant.ToUnixTimeSeconds() / (long)Step.TotalSeconds;

    public static string Code(ReadOnlySpan<byte> secret, long step, int digits = Digits)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);

        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.HashData(secret, counter, hash);

        // RFC 4226 dynamic truncation.
        var offset = hash[^1] & 0x0F;
        var binary = (BinaryPrimitives.ReadInt32BigEndian(hash[offset..]) & 0x7FFFFFFF) % (int)Math.Pow(10, digits);

        return binary.ToString(new string('0', digits));
    }

    /// <summary>
    /// The step the code belongs to: the current one or one either side (clock drift), and later
    /// than <paramref name="lastUsedStep"/> (a code is accepted once). Null: no match.
    /// </summary>
    public static long? Match(ReadOnlySpan<byte> secret, string? code, DateTimeOffset now, long lastUsedStep)
    {
        var digits = (code ?? string.Empty).Replace(" ", string.Empty);

        if (digits.Length != Digits || !digits.All(char.IsAsciiDigit))
        {
            return null;
        }

        var given = Encoding.ASCII.GetBytes(digits);
        var current = StepAt(now);
        long? matched = null;

        // Every candidate is compared, in constant time, whatever matched first.
        for (var step = current - 1; step <= current + 1; step++)
        {
            if (CryptographicOperations.FixedTimeEquals(given, Encoding.ASCII.GetBytes(Code(secret, step))) && step > lastUsedStep)
            {
                matched ??= step;
            }
        }

        return matched;
    }
}

/// <summary>RFC 4648 base32, how TOTP secrets are written down.</summary>
internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder((bytes.Length + 4) / 5 * 8);
        int buffer = 0, bits = 0;

        foreach (var value in bytes)
        {
            buffer = ((buffer << 8) | value) & 0xFFF;
            bits += 8;

            while (bits >= 5)
            {
                builder.Append(Alphabet[(buffer >> (bits - 5)) & 0x1F]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            builder.Append(Alphabet[(buffer << (5 - bits)) & 0x1F]);
        }

        return builder.ToString();
    }

    /// <summary>Case-insensitive; padding, spaces and hyphens are ignored.</summary>
    public static bool TryDecode(string value, out byte[] bytes)
    {
        var output = new List<byte>(value.Length * 5 / 8);
        int buffer = 0, bits = 0;

        foreach (var character in value)
        {
            if (character is '=' or ' ' or '-')
            {
                continue;
            }

            var index = Alphabet.IndexOf(char.ToUpperInvariant(character));

            if (index < 0)
            {
                bytes = [];
                return false;
            }

            buffer = ((buffer << 5) | index) & 0xFFF;
            bits += 5;

            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        bytes = [.. output];
        return true;
    }
}
