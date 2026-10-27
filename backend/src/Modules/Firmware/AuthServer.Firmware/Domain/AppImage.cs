using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using AuthServer.Shared;

namespace AuthServer.Firmware.Domain;

/// <summary>
/// An ESP-IDF app image, checked the way the firmware's own OTA check does (ota/image_check.h)
/// before it is kept: a 24-byte header (magic 0xE9, segment count at 1, chip id at 12, hash-appended
/// flag at 23), segments (load address, length, data; the first opens with esp_app_desc_t), zero
/// padding up to a 16-byte boundary whose last byte is the XOR of all segment data seeded with 0xEF,
/// then the SHA-256 of everything before it when hash-appended. Nothing after that.
/// </summary>
internal sealed record AppImage(string Version, string Project, string Built, string Idf)
{
    private const byte Magic = 0xE9;
    private const ushort ChipEsp32S3 = 9;
    private const int HeaderSize = 24;
    private const int SegmentHeaderSize = 8;
    private const int MaxSegments = 16;
    private const uint DescriptorMagic = 0xABCD5432;
    private const int DescriptorSize = 180;
    private const byte ChecksumSeed = 0xEF;

    public static Result<AppImage> Parse(ReadOnlySpan<byte> file, string project)
    {
        if (file.Length < HeaderSize || file[0] != Magic
            || BinaryPrimitives.ReadUInt16LittleEndian(file[12..]) != ChipEsp32S3
            || file[1] is 0 or > MaxSegments)
        {
            return FirmwareErrors.ImageInvalid;
        }

        var segments = file[1];
        var hashAppended = file[23] != 0;
        var position = HeaderSize;
        var checksum = ChecksumSeed;
        AppImage? image = null;

        for (var index = 0; index < segments; index++)
        {
            if (file.Length - position < SegmentHeaderSize)
            {
                return FirmwareErrors.ImageInvalid;
            }

            var length = BinaryPrimitives.ReadUInt32LittleEndian(file[(position + 4)..]);
            position += SegmentHeaderSize;

            if (length > (uint)(file.Length - position))
            {
                return FirmwareErrors.ImageInvalid;
            }

            var data = file.Slice(position, (int)length);

            if (index == 0)
            {
                if (data.Length < DescriptorSize || BinaryPrimitives.ReadUInt32LittleEndian(data) != DescriptorMagic)
                {
                    return FirmwareErrors.ImageInvalid;
                }

                image = new AppImage(
                    Field(data.Slice(16, 32)),
                    Field(data.Slice(48, 32)),
                    $"{Field(data.Slice(96, 16))} {Field(data.Slice(80, 16))}".Trim(),
                    Field(data.Slice(112, 32)));
            }

            foreach (var value in data)
            {
                checksum ^= value;
            }

            position += data.Length;
        }

        var paddedEnd = (position + 16) & ~15;
        var end = paddedEnd + (hashAppended ? SHA256.HashSizeInBytes : 0);

        if (end != file.Length || file[paddedEnd - 1] != checksum
            || (hashAppended && !SHA256.HashData(file[..paddedEnd]).AsSpan().SequenceEqual(file[paddedEnd..])))
        {
            return FirmwareErrors.ImageInvalid;
        }

        if (image is null || image.Project != project
            || image.Version.Length == 0 || !image.Version.All(character => character is > ' ' and <= '~'))
        {
            return FirmwareErrors.ImageInvalid;
        }

        return image;
    }

    /// <summary>A NUL-terminated char[] of the descriptor.</summary>
    private static string Field(ReadOnlySpan<byte> field)
    {
        var length = field.IndexOf((byte)0);
        return Encoding.ASCII.GetString(length < 0 ? field : field[..length]);
    }
}
