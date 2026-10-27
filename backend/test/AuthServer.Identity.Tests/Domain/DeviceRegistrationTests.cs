using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Tests.Devices;

namespace AuthServer.Identity.Tests.Domain;

// The firmware's registration JWS (architecture.md §4.6).
public sealed class DeviceRegistrationTests
{
    private const string Email = "Owner@Example.com";
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_correctly_signed_registration_verifies()
    {
        using var device = new VirtualDevice(mac: "c4:9e:7e:29:22:e8");

        var result = DeviceRegistration.Verify(device.Registration(Email, Now), Now);

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        result.Value.Email.ShouldBe(Email);
        result.Value.Serial.ShouldBe(device.Serial);
        result.Value.Mac.ShouldBe("c4:9e:7e:29:22:e8");
        result.Value.TotpSecret.ShouldBe(device.TotpSecret);
        result.Value.PublicKey.ShouldBe(Base64Url.DecodeFromChars(device.PublicKey));
    }

    [Fact]
    public void The_spec_example_shape_parses()
    {
        // An upper-case serial and MAC from another firmware build normalize to lower case.
        using var device = new VirtualDevice("524E1668FC0E2FEB350FA0DD7CF4E4BF", "C4:9E:7E:29:22:E8");

        var result = DeviceRegistration.Verify(device.Registration(Email, Now), Now);

        result.Value!.Serial.ShouldBe("524e1668fc0e2feb350fa0dd7cf4e4bf");
        result.Value.Mac.ShouldBe("c4:9e:7e:29:22:e8");
    }

    [Theory]
    [InlineData(-600)]
    [InlineData(600)]
    public void An_issue_time_within_ten_minutes_is_fresh(int seconds)
    {
        using var device = new VirtualDevice();

        DeviceRegistration.Verify(device.Registration(Email, Now.AddSeconds(seconds)), Now).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(-601)]
    [InlineData(601)]
    public void An_issue_time_further_off_is_stale(int seconds)
    {
        using var device = new VirtualDevice();

        DeviceRegistration.Verify(device.Registration(Email, Now.AddSeconds(seconds)), Now).Error.ShouldBe(IdentityErrors.RegistrationStale);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("serial")]
    [InlineData("mac")]
    [InlineData("totp")]
    [InlineData("iat")]
    public void Changing_any_claim_after_signing_breaks_the_signature(string claim)
    {
        using var device = new VirtualDevice();
        var token = device.Registration(Email, Now);
        var claims = device.Claims(Email, Now);
        using var other = new VirtualDevice();

        claims[claim] = claim switch
        {
            "email" => "someone@example.com",
            "serial" => other.Serial,
            "mac" => other.Mac,
            "totp" => other.TotpSecretBase32,
            _ => Now.AddSeconds(1).ToUnixTimeSeconds(),
        };

        var tampered = string.Join('.', token.Split('.')[0], Payload(device.Token(claims)), token.Split('.')[2]);

        DeviceRegistration.Verify(tampered, Now).Error.ShouldBe(IdentityErrors.RegistrationRefused);
    }

    [Fact]
    public void A_signature_by_another_key_than_pub_is_refused()
    {
        using var device = new VirtualDevice();
        using var other = new VirtualDevice();

        var token = device.Token(device.Claims(Email, Now), signWith: other.Key);

        DeviceRegistration.Verify(token, Now).Error.ShouldBe(IdentityErrors.RegistrationRefused);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("HS256")]
    [InlineData("ES384")]
    public void Only_ES256_is_accepted(string algorithm)
    {
        using var device = new VirtualDevice();

        DeviceRegistration.Verify(device.Token(device.Claims(Email, Now), algorithm), Now)
            .Error.ShouldBe(IdentityErrors.RegistrationInvalid);
    }

    [Fact]
    public void A_DER_signature_is_not_a_JWS_signature()
    {
        using var device = new VirtualDevice();
        var token = device.Registration(Email, Now);
        var parts = token.Split('.');
        var der = device.Key.SignData(
            Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        DeviceRegistration.Verify($"{parts[0]}.{parts[1]}.{Base64Url.EncodeToString(der)}", Now)
            .Error.ShouldBe(IdentityErrors.RegistrationInvalid);
    }

    [Theory]
    [InlineData("email", "")]
    [InlineData("email", "no-at-sign")]
    [InlineData("email", "two@at@signs.com")]
    [InlineData("email", "ü@example.com")]
    [InlineData("serial", "524e1668fc0e2feb350fa0dd7cf4e4b")]
    [InlineData("serial", "524e1668fc0e2feb350fa0dd7cf4e4bg")]
    [InlineData("mac", "c4:9e:7e:29:22")]
    [InlineData("mac", "c4:9e:7e:29:22:zz")]
    [InlineData("totp", "!!!")]
    [InlineData("totp", "MZXW6YTB")]
    [InlineData("pub", "AAAA")]
    public void A_malformed_claim_is_invalid_rather_than_refused(string claim, string value)
    {
        using var device = new VirtualDevice();
        var claims = device.Claims(Email, Now);
        claims[claim] = value;

        DeviceRegistration.Verify(device.Token(claims), Now).Error.ShouldBe(IdentityErrors.RegistrationInvalid);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("serial")]
    [InlineData("mac")]
    [InlineData("totp")]
    [InlineData("pub")]
    [InlineData("iat")]
    public void A_missing_claim_is_invalid(string claim)
    {
        using var device = new VirtualDevice();
        var claims = device.Claims(Email, Now);
        claims.Remove(claim);

        DeviceRegistration.Verify(device.Token(claims), Now).Error.ShouldBe(IdentityErrors.RegistrationInvalid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a token")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    [InlineData("!!.!!.!!")]
    public void Something_that_is_not_a_compact_JWS_is_invalid(string token)
    {
        DeviceRegistration.Verify(token, Now).Error.ShouldBe(IdentityErrors.RegistrationInvalid);
    }

    [Fact]
    public void A_point_that_is_not_on_the_curve_is_invalid()
    {
        using var device = new VirtualDevice();
        var point = Base64Url.DecodeFromChars(device.PublicKey);
        point[^1] ^= 0x01;
        var claims = device.Claims(Email, Now);
        claims["pub"] = Base64Url.EncodeToString(point);

        DeviceRegistration.Verify(device.Token(claims), Now).Error.ShouldBe(IdentityErrors.RegistrationInvalid);
    }

    private static string Payload(string token) => token.Split('.')[1];
}
