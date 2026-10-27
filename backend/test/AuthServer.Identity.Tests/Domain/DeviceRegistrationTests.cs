using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Tests.Devices;

namespace AuthServer.Identity.Tests.Domain;

public sealed class DeviceRegistrationTests
{
    private const string Email = "Owner@Example.com";

    [Fact]
    public void A_correctly_signed_registration_verifies()
    {
        using var device = new VirtualDevice(mac: "aa-bb-cc-dd-ee-ff");
        var body = device.Registration(Email);

        var result = Verify(body);

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        result.Value.Email.ShouldBe(Email);
        result.Value.HardwareId.ShouldBe(device.HardwareId);
        result.Value.Mac.ShouldBe("AA:BB:CC:DD:EE:FF");
        result.Value.TotpSecret.ShouldBe(device.TotpSecret);
        result.Value.PublicKey.Length.ShouldBe(65);
        result.Value.PublicKey[0].ShouldBe((byte)0x04);
    }

    [Fact]
    public void A_DER_signature_is_accepted_as_well_as_raw()
    {
        using var device = new VirtualDevice();
        var body = device.Registration(Email);
        var message = VirtualDevice.Message(Email, device.HardwareId, device.Mac, body.TotpSecret, body.PublicKey);
        var der = device.Key.SignData(
            Encoding.UTF8.GetBytes(message), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        Verify(body with { Signature = Base64Url.EncodeToString(der) }).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_64_byte_public_key_without_the_prefix_is_accepted()
    {
        using var device = new VirtualDevice();
        var raw = Base64Url.DecodeFromChars(device.PublicKey)[1..];
        var publicKey = Base64Url.EncodeToString(raw);
        var message = VirtualDevice.Message(Email, device.HardwareId, device.Mac, device.TotpSecretBase32, publicKey);
        var body = device.Registration(Email) with { PublicKey = publicKey, Signature = device.Sign(message) };

        var result = Verify(body);

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        result.Value.PublicKey.ShouldBe(Base64Url.DecodeFromChars(device.PublicKey));
    }

    [Theory]
    [InlineData("email")]
    [InlineData("hardwareId")]
    [InlineData("mac")]
    [InlineData("totpSecret")]
    [InlineData("publicKey")]
    public void Changing_any_signed_field_breaks_the_signature(string field)
    {
        using var device = new VirtualDevice();
        using var other = new VirtualDevice();
        var body = device.Registration(Email);

        var tampered = field switch
        {
            "email" => body with { Email = "someone@example.com" },
            "hardwareId" => body with { HardwareId = body.HardwareId + "0" },
            "mac" => body with { Mac = other.Mac },
            "totpSecret" => body with { TotpSecret = other.TotpSecretBase32 },
            _ => body with { PublicKey = other.PublicKey },
        };

        Verify(tampered).Error.ShouldBe(IdentityErrors.RegistrationRefused);
    }

    [Fact]
    public void A_signature_by_another_key_is_refused()
    {
        using var device = new VirtualDevice();
        using var other = new VirtualDevice();
        var body = device.Registration(Email);
        var message = VirtualDevice.Message(Email, body.HardwareId, body.Mac, body.TotpSecret, body.PublicKey);

        Verify(body with { Signature = other.Sign(message) }).Error.ShouldBe(IdentityErrors.RegistrationRefused);
    }

    [Theory]
    [InlineData("email", "")]
    [InlineData("email", "not-an-address")]
    [InlineData("hardwareId", "")]
    [InlineData("hardwareId", "has space")]
    [InlineData("mac", "AA:BB:CC:DD:EE")]
    [InlineData("mac", "GG:BB:CC:DD:EE:FF")]
    [InlineData("totpSecret", "!!!")]
    [InlineData("totpSecret", "MZXW6YTB")]
    [InlineData("publicKey", "AAAA")]
    [InlineData("signature", "AAAA")]
    public void A_malformed_field_is_invalid_rather_than_refused(string field, string value)
    {
        using var device = new VirtualDevice();
        var body = device.Registration(Email);

        var malformed = field switch
        {
            "email" => body with { Email = value },
            "hardwareId" => body with { HardwareId = value },
            "mac" => body with { Mac = value },
            "totpSecret" => body with { TotpSecret = value },
            "publicKey" => body with { PublicKey = value },
            _ => body with { Signature = value },
        };

        Verify(malformed).Error.ShouldBe(IdentityErrors.RegistrationInvalid);
    }

    [Fact]
    public void A_point_that_is_not_on_the_curve_is_invalid()
    {
        using var device = new VirtualDevice();
        var point = Base64Url.DecodeFromChars(device.PublicKey);
        point[^1] ^= 0x01;
        var body = device.Registration(Email) with { PublicKey = Base64Url.EncodeToString(point) };

        Verify(body).Error.ShouldBe(IdentityErrors.RegistrationInvalid);
    }

    private static AuthServer.Shared.Result<DeviceRegistration> Verify(VirtualDevice.RegistrationBody body) =>
        DeviceRegistration.Verify(body.Email, body.HardwareId, body.Mac, body.TotpSecret, body.PublicKey, body.Signature);
}
