using AuthServer.Firmware.Domain;

namespace AuthServer.Firmware.Tests.Domain;

// The server keeps only an image the device's own check would accept (firmware ota/image_check.h).
public sealed class AppImageTests
{
    private const string Project = "esp32-authenticator";

    [Fact]
    public void An_image_is_named_by_its_descriptor()
    {
        var image = AppImage.Parse(new FirmwareImage { Version = "0.6.0-3-gabc1234-dirty" }.Bytes(), Project);

        image.IsSuccess.ShouldBeTrue();
        image.Value!.Version.ShouldBe("0.6.0-3-gabc1234-dirty");
        image.Value.Built.ShouldBe("Oct 27 2026 21:07:30");
        image.Value.Idf.ShouldBe("v6.0");
    }

    [Fact]
    public void An_image_without_the_hash_is_fine()
    {
        AppImage.Parse(new FirmwareImage { HashAppended = false }.Bytes(), Project).IsSuccess.ShouldBeTrue();
    }

    public static TheoryData<FirmwareImage> Broken => new()
    {
        new FirmwareImage { Magic = 0x00 },
        new FirmwareImage { ChipId = 2 },
        new FirmwareImage { DescriptorMagic = 0 },
        new FirmwareImage { Project = "another-project" },
        new FirmwareImage { SegmentLengths = [] },
        new FirmwareImage { SegmentLengths = [100] },
        new FirmwareImage { BreakChecksum = true },
        new FirmwareImage { BreakHash = true },
        new FirmwareImage { ExtraBytes = 1 },
        new FirmwareImage { Version = "" },
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public void A_broken_or_foreign_image_is_refused(FirmwareImage file)
    {
        var image = AppImage.Parse(file.Bytes(), Project);

        image.IsSuccess.ShouldBeFalse();
        image.Error!.Code.ShouldBe("firmware.image-invalid");
    }

    [Fact]
    public void A_truncated_image_is_refused()
    {
        var bytes = new FirmwareImage().Bytes();

        foreach (var length in new[] { 0, 10, 40, 300, bytes.Length - 1 })
        {
            AppImage.Parse(bytes.AsSpan(0, length), Project).IsSuccess.ShouldBeFalse();
        }
    }
}
