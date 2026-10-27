using System.Text;
using AuthServer.Identity.Domain;

namespace AuthServer.Identity.Tests.Domain;

public sealed class TotpTests
{
    // RFC 6238 appendix B, SHA-1: the 20-byte ASCII secret "12345678901234567890", 8 digits.
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    [InlineData(20000000000L, "65353130")]
    public void Code_matches_the_RFC_6238_vectors(long unixSeconds, string expected)
    {
        var step = Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));

        Totp.Code(RfcSecret, step, digits: 8).ShouldBe(expected);
    }

    [Fact]
    public void The_default_code_is_the_last_six_digits()
    {
        var step = Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(59));

        Totp.Code(RfcSecret, step).ShouldBe("287082");
    }

    [Fact]
    public void Match_accepts_the_current_step_and_one_either_side()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var step = Totp.StepAt(now);

        Totp.Match(RfcSecret, Totp.Code(RfcSecret, step - 1), now, lastUsedStep: 0).ShouldBe(step - 1);
        Totp.Match(RfcSecret, Totp.Code(RfcSecret, step), now, lastUsedStep: 0).ShouldBe(step);
        Totp.Match(RfcSecret, Totp.Code(RfcSecret, step + 1), now, lastUsedStep: 0).ShouldBe(step + 1);

        Totp.Match(RfcSecret, Totp.Code(RfcSecret, step - 2), now, lastUsedStep: 0).ShouldBeNull();
        Totp.Match(RfcSecret, Totp.Code(RfcSecret, step + 2), now, lastUsedStep: 0).ShouldBeNull();
    }

    [Fact]
    public void Match_refuses_a_step_at_or_before_the_last_used_one()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var step = Totp.StepAt(now);
        var code = Totp.Code(RfcSecret, step);

        Totp.Match(RfcSecret, code, now, lastUsedStep: step).ShouldBeNull();
        Totp.Match(RfcSecret, code, now, lastUsedStep: step - 1).ShouldBe(step);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public void Match_refuses_anything_that_is_not_six_digits(string? code)
    {
        Totp.Match(RfcSecret, code, DateTimeOffset.FromUnixTimeSeconds(59), lastUsedStep: 0).ShouldBeNull();
    }

    [Fact]
    public void Match_ignores_surrounding_and_inner_spaces()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(59);
        var step = Totp.StepAt(now);

        Totp.Match(RfcSecret, " 287 082 ", now, lastUsedStep: 0).ShouldBe(step);
    }
}
