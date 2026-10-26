using AuthServer.Host.Startup;

namespace AuthServer.Host.Tests;

public sealed class ProductVersionTests
{
    [Theory]
    [InlineData("0.1.0+abc123", "0.1.0", "abc123")]
    [InlineData("0.1.0", "0.1.0", null)]
    [InlineData("0.1.0+", "0.1.0", null)]
    [InlineData("1.2.3-rc.1+deadbeef", "1.2.3-rc.1", "deadbeef")]
    [InlineData(null, "0.0.0", null)]
    [InlineData("", "0.0.0", null)]
    public void Parse_splits_version_and_commit(string? informational, string version, string? commit)
    {
        ProductVersion.Parse(informational).ShouldBe((version, commit));
    }
}
