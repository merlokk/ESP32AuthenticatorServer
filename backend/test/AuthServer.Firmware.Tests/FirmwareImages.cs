using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AuthServer.Firmware.Tests;

/// <summary>
/// An ESP-IDF app image as idf.py builds it (the firmware's ota/image_check.h): a 24-byte header,
/// segments (the first opens with esp_app_desc_t), zero padding to 16 bytes whose last byte is the
/// XOR checksum, the SHA-256 of all that. Segment data is random: the server never runs it.
/// </summary>
public sealed record FirmwareImage
{
    public string Version { get; init; } = "0.6.0-0-gabc1234";

    public string Project { get; init; } = "esp32-authenticator";

    public ushort ChipId { get; init; } = 9;

    public byte Magic { get; init; } = 0xE9;

    public uint DescriptorMagic { get; init; } = 0xABCD5432;

    public int[] SegmentLengths { get; init; } = [512, 300];

    public bool HashAppended { get; init; } = true;

    public bool BreakChecksum { get; init; }

    public bool BreakHash { get; init; }

    public int ExtraBytes { get; init; }

    public byte[] Bytes()
    {
        using var image = new MemoryStream();
        var header = new byte[24];
        header[0] = Magic;
        header[1] = (byte)SegmentLengths.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), ChipId);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(17), 0xFFFF);
        header[23] = HashAppended ? (byte)1 : (byte)0;
        image.Write(header);

        byte checksum = 0xEF;

        for (var index = 0; index < SegmentLengths.Length; index++)
        {
            var data = RandomNumberGenerator.GetBytes(SegmentLengths[index]);

            // A first segment too short for the descriptor is a broken image: no descriptor then.
            if (index == 0 && data.Length >= 256)
            {
                Array.Clear(data, 0, 256);
                BinaryPrimitives.WriteUInt32LittleEndian(data, DescriptorMagic);
                Encoding.ASCII.GetBytes(Version).CopyTo(data, 16);
                Encoding.ASCII.GetBytes(Project).CopyTo(data, 48);
                Encoding.ASCII.GetBytes("21:07:30").CopyTo(data, 80);
                Encoding.ASCII.GetBytes("Oct 27 2026").CopyTo(data, 96);
                Encoding.ASCII.GetBytes("v6.0").CopyTo(data, 112);
            }

            var segment = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(segment, 0x3C000020);
            BinaryPrimitives.WriteUInt32LittleEndian(segment.AsSpan(4), (uint)data.Length);
            image.Write(segment);
            image.Write(data);

            foreach (var value in data)
            {
                checksum ^= value;
            }
        }

        var end = (image.Length + 16) & ~15L;
        image.Write(new byte[end - image.Length - 1]);
        image.WriteByte(BreakChecksum ? (byte)~checksum : checksum);

        if (HashAppended)
        {
            var hash = SHA256.HashData(image.ToArray());

            if (BreakHash)
            {
                hash[0] ^= 1;
            }

            image.Write(hash);
        }

        image.Write(new byte[ExtraBytes]);

        return image.ToArray();
    }
}
