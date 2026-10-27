using AuthServer.Identity.Domain;

namespace AuthServer.Identity.Tests.Domain;

// The firmware's version is git's (cmake/version.cmake): x.y.z-<commits>-g<hash>[-dirty], or
// x.y.z-nogit; kept as reported. Which release is offered: FirmwareTests.
public sealed class FirmwareVersionTests
{
    [Theory]
    [InlineData("0.5.0-174-g2f84ffa", true)]
    [InlineData("0.5.0", true)]
    [InlineData("", false)]
    [InlineData("0123456789012345678901234567890123", false)]
    [InlineData("0.5.0\n", false)]
    public void A_reported_version_is_1_to_32_printable_characters(string reported, bool valid) =>
        FirmwareVersion.IsValid(reported).ShouldBe(valid);

    [Theory]
    [InlineData("x4pro", true)]
    [InlineData("dev", true)]
    [InlineData("", false)]
    [InlineData("a-board-name-too-long", false)]
    [InlineData("x4 pro", false)]
    public void A_board_is_1_to_16_printable_characters(string board, bool valid) =>
        FirmwareVersion.IsValidBoard(board).ShouldBe(valid);
}
