using AuthServer.Sync.Domain;

namespace AuthServer.Sync.Tests.Domain;

public sealed class StoreHeaderTests
{
    [Fact]
    public void Reads_every_field_the_server_uses()
    {
        var file = new StoreFile
        {
            Kind = 1,
            Version = 0xFFFF_FFFE,
            Modified = 1_793_000_000,
            KdfIterations = 600_000,
            KeyId = Convert.FromHexString("1122334455667788"),
            StoreId = Convert.FromHexString("0011223344556677"),
            SeedId = Convert.FromHexString("deadbeef"),
            Nonce = Convert.FromHexString("00112233445566778899aabb"),
            PayloadLength = 100,
        };

        var header = StoreHeader.Parse(file.Bytes());

        header.IsSuccess.ShouldBeTrue(header.Error?.Message);
        header.Value!.Kind.ShouldBe(StoreKind.Fido);
        header.Value.Cipher.ShouldBe(StoreCipher.AesGcm);
        header.Value.Version.ShouldBe(0xFFFF_FFFEu);
        header.Value.Length.ShouldBe(100u);
        header.Value.Modified.ShouldBe(1_793_000_000);
        header.Value.KdfIterations.ShouldBe(600_000u);
        header.Value.KeyId.ShouldBe("1122334455667788");
        header.Value.StoreId.ShouldBe("0011223344556677");
        header.Value.SeedId.ShouldBe("deadbeef");
        header.Value.Nonce.ShouldBe("00112233445566778899aabb");
    }

    [Fact]
    public void An_unknown_time_is_zero()
    {
        StoreHeader.Parse(new StoreFile { Modified = 0 }.Bytes()).Value!.Modified.ShouldBe(0);
    }

    public static TheoryData<string, StoreFile> NotAStore => new()
    {
        { "no magic", new StoreFile { Magic = "XXXX" } },
        { "another format", new StoreFile { Format = 2 } },
        { "an unknown kind", new StoreFile { Kind = 3 } },
        { "an unknown cipher", new StoreFile { Cipher = 2 } },
        { "a length that does not add up", new StoreFile { ExtraBytes = 1 } },
    };

    [Theory]
    [MemberData(nameof(NotAStore))]
    public void Refuses_a_file_that_is_not_a_store(string why, StoreFile file)
    {
        StoreHeader.Parse(file.Bytes()).Error.ShouldBe(SyncErrors.FileInvalid, why);
    }

    [Fact]
    public void Refuses_a_truncated_header()
    {
        StoreHeader.Parse(new StoreFile().Bytes()[..79]).Error.ShouldBe(SyncErrors.FileInvalid);
    }

    [Fact]
    public void Refuses_a_plain_store_the_server_could_read()
    {
        StoreHeader.Parse(new StoreFile { Cipher = 0 }.Bytes()).Error.ShouldBe(SyncErrors.FilePlain);
    }
}
