using System.Text;
using AuthServer.Identity.Domain;

namespace AuthServer.Identity.Tests.Domain;

public sealed class Base32Tests
{
    // RFC 4648 section 10.
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY======")]
    [InlineData("fo", "MZXQ====")]
    [InlineData("foo", "MZXW6===")]
    [InlineData("foob", "MZXW6YQ=")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI======")]
    public void Decodes_the_RFC_4648_vectors(string plain, string encoded)
    {
        Base32.TryDecode(encoded, out var bytes).ShouldBeTrue();
        Encoding.ASCII.GetString(bytes).ShouldBe(plain);
    }

    [Fact]
    public void Decoding_ignores_case_padding_spaces_and_hyphens()
    {
        Base32.TryDecode("mzxw 6ytb-oi", out var bytes).ShouldBeTrue();
        Encoding.ASCII.GetString(bytes).ShouldBe("foobar");
    }

    [Theory]
    [InlineData("MZXW1")]
    [InlineData("MZXW8")]
    [InlineData("MZ!W")]
    public void Refuses_characters_outside_the_alphabet(string encoded)
    {
        Base32.TryDecode(encoded, out _).ShouldBeFalse();
    }

    [Fact]
    public void Encode_round_trips()
    {
        var bytes = Enumerable.Range(0, 37).Select(value => (byte)(value * 7)).ToArray();

        Base32.TryDecode(Base32.Encode(bytes), out var decoded).ShouldBeTrue();
        decoded.ShouldBe(bytes);
    }
}
