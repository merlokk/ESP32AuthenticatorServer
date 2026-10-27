using System.Security.Cryptography;

namespace AuthServer.Identity.Infrastructure.Devices;

/// <summary>
/// AES-256-GCM over the TOTP secrets at rest, with a key from configuration (a cluster secret),
/// not from the database: a dump of the database alone does not yield a code.
/// Layout: version (1) || nonce (12) || tag (16) || ciphertext.
/// </summary>
internal sealed class TotpSecretProtector
{
    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public TotpSecretProtector(string base64Key)
    {
        byte[] key;

        try
        {
            key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException)
        {
            key = [];
        }

        if (key.Length != 32)
        {
            throw new InvalidOperationException(
                "Identity:TotpEncryptionKey must be 32 random bytes in base64 (openssl rand -base64 32). "
                + "It encrypts the devices' TOTP secrets; losing it means every device registers again.");
        }

        _key = key;
    }

    public byte[] Protect(ReadOnlySpan<byte> secret)
    {
        var output = new byte[1 + NonceSize + TagSize + secret.Length];
        output[0] = Version;

        var nonce = output.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, secret, output.AsSpan(1 + NonceSize + TagSize), output.AsSpan(1 + NonceSize, TagSize), [Version]);

        return output;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> stored)
    {
        if (stored.Length < 1 + NonceSize + TagSize || stored[0] != Version)
        {
            throw new CryptographicException("Not a protected TOTP secret.");
        }

        var secret = new byte[stored.Length - 1 - NonceSize - TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(stored.Slice(1, NonceSize), stored[(1 + NonceSize + TagSize)..], stored.Slice(1 + NonceSize, TagSize), secret, [Version]);

        return secret;
    }
}
