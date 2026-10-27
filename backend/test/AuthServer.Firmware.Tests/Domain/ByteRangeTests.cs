using AuthServer.Firmware.Domain;

namespace AuthServer.Firmware.Tests.Domain;

// The OTA download in pieces: one Range per request (RFC 9110 §14), bytes only.
public sealed class ByteRangeTests
{
    [Theory]
    [InlineData("bytes=0-99", 0L, 100L)]
    [InlineData("bytes=900-", 900L, 100L)]
    [InlineData("bytes=900-5000", 900L, 100L)]
    [InlineData("bytes=-10", 990L, 10L)]
    [InlineData("bytes=-5000", 0L, 1000L)]
    [InlineData("bytes = 10-19", 10L, 10L)]
    public void A_single_range_is_read(string header, long offset, long length)
    {
        ByteRange.Parse(header, 1000).ShouldBe(new ByteRange.Outcome(ByteRange.Kind.Partial, offset, length));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("items=0-1")]
    [InlineData("bytes=0-1,5-6")]
    [InlineData("bytes=a-b")]
    [InlineData("bytes=10-5")]
    public void Anything_else_is_the_whole_file(string? header)
    {
        ByteRange.Parse(header, 1000).Kind.ShouldBe(ByteRange.Kind.Whole);
    }

    [Theory]
    [InlineData("bytes=1000-")]
    [InlineData("bytes=5000-6000")]
    [InlineData("bytes=-0")]
    public void A_range_past_the_end_is_unsatisfiable(string header)
    {
        ByteRange.Parse(header, 1000).Kind.ShouldBe(ByteRange.Kind.Unsatisfiable);
    }
}
