using AuthServer.Identity.Domain;

namespace AuthServer.Identity.Tests.Domain;

// The firmware's version is git's (cmake/version.cmake): x.y.z-<commits>-g<hash>[-dirty], or
// x.y.z-nogit. A release is x.y.z; only a higher release is offered.
public sealed class FirmwareVersionTests
{
    [Theory]
    [InlineData("0.5.0-174-g2f84ffa", "0.6.0", "0.6.0")]
    [InlineData("0.5.0-174-g2f84ffa-dirty", "0.6.0", "0.6.0")]
    [InlineData("0.5.0-nogit", "0.6.0", "0.6.0")]
    [InlineData("0.5.0", "0.5.1", "0.5.1")]
    [InlineData("0.9.9-1-gabc1234", "1.0.0", "1.0.0")]
    public void A_higher_release_is_offered(string reported, string latest, string offered) =>
        FirmwareVersion.Update(reported, latest).ShouldBe(offered);

    [Theory]
    [InlineData("0.6.0-0-gabc1234", "0.6.0")]
    [InlineData("0.6.0-12-gabc1234", "0.6.0")]
    [InlineData("0.6.1-3-gabc1234", "0.6.0")]
    [InlineData("0.10.0-1-gabc1234", "0.9.0")]
    public void The_same_or_a_later_build_is_not(string reported, string latest) =>
        FirmwareVersion.Update(reported, latest).ShouldBeNull();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    public void Nothing_is_offered_without_a_release_configured(string latest) =>
        FirmwareVersion.Update("0.5.0-1-gabc1234", latest).ShouldBeNull();

    [Fact]
    public void A_version_the_server_cannot_read_is_offered_the_release() =>
        FirmwareVersion.Update("custom-build", "0.6.0").ShouldBe("0.6.0");

    [Theory]
    [InlineData("0.5.0-174-g2f84ffa", true)]
    [InlineData("0.5.0", true)]
    [InlineData("", false)]
    [InlineData("0123456789012345678901234567890123", false)]
    [InlineData("0.5.0\n", false)]
    public void A_reported_version_is_1_to_32_printable_characters(string reported, bool valid) =>
        FirmwareVersion.IsValid(reported).ShouldBe(valid);
}
