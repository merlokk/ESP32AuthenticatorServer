using AuthServer.Sync.Domain;

namespace AuthServer.Sync.Tests.Domain;

// The counter decides, never the time: clocks drift.
public sealed class UploadRuleTests
{
    [Fact]
    public void The_first_copy_is_accepted() => UploadRule.Accepts(1, head: null).ShouldBeTrue();

    [Theory]
    [InlineData(6u, 5u, true)]
    [InlineData(5u, 5u, false)]
    [InlineData(4u, 5u, false)]
    [InlineData(uint.MaxValue, uint.MaxValue - 1, true)]
    public void A_copy_is_accepted_only_above_the_head(uint upload, uint head, bool accepted) =>
        UploadRule.Accepts(upload, head).ShouldBe(accepted);

    [Theory]
    [InlineData(5u, 4u, true)]
    [InlineData(5u, 5u, false)]
    [InlineData(5u, 6u, false)]
    [InlineData(5u, null, true)]
    public void A_download_is_served_only_above_what_the_device_has(uint head, uint? above, bool served) =>
        UploadRule.Serves(head, above).ShouldBe(served);
}
