using System.Security.Cryptography;
using AuthServer.Identity.Infrastructure.Devices;

namespace AuthServer.Identity.Tests.Infrastructure;

public sealed class TotpSecretProtectorTests
{
    private static readonly string Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void A_secret_round_trips_and_is_not_stored_in_the_clear()
    {
        var protector = new TotpSecretProtector(Key);
        var secret = RandomNumberGenerator.GetBytes(20);

        var stored = protector.Protect(secret);

        stored.AsSpan().IndexOf(secret).ShouldBe(-1);
        protector.Unprotect(stored).ShouldBe(secret);
    }

    [Fact]
    public void The_same_secret_protects_differently_each_time()
    {
        var protector = new TotpSecretProtector(Key);
        var secret = RandomNumberGenerator.GetBytes(20);

        protector.Protect(secret).ShouldNotBe(protector.Protect(secret));
    }

    [Fact]
    public void A_tampered_value_does_not_unprotect()
    {
        var protector = new TotpSecretProtector(Key);
        var stored = protector.Protect(RandomNumberGenerator.GetBytes(20));
        stored[^1] ^= 0x01;

        Should.Throw<CryptographicException>(() => protector.Unprotect(stored));
    }

    [Fact]
    public void Another_key_does_not_unprotect()
    {
        var stored = new TotpSecretProtector(Key).Protect(RandomNumberGenerator.GetBytes(20));
        var other = new TotpSecretProtector(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

        Should.Throw<CryptographicException>(() => other.Unprotect(stored));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")]
    public void A_missing_or_short_key_names_the_setting(string key)
    {
        var refusal = Should.Throw<InvalidOperationException>(() => new TotpSecretProtector(key));

        refusal.Message.ShouldContain("Identity:TotpEncryptionKey");
    }
}
