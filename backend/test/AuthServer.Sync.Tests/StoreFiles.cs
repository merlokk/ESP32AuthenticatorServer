using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AuthServer.Sync.Tests;

/// <summary>
/// A store file as the firmware writes it (store_header.h): an 80-byte little-endian header, the
/// payload, and a 16-byte GCM tag when encrypted. The payload is random: the server never reads it.
/// </summary>
public sealed record StoreFile
{
    public byte Kind { get; init; } = 2;

    public byte Cipher { get; init; } = 1;

    public byte Format { get; init; } = 3;

    public string Magic { get; init; } = "ESAF";

    public uint Version { get; init; } = 1;

    public long Modified { get; init; } = 1_790_000_000;

    public uint KdfIterations { get; init; } = 200_000;

    public byte[] KeyId { get; init; } = Convert.FromHexString("0102030405060708");

    public byte[] StoreId { get; init; } = Convert.FromHexString("a1a2a3a4a5a6a7a8");

    public byte[] SeedId { get; init; } = Convert.FromHexString("c1c2c3c4");

    public int PayloadLength { get; init; } = 64;

    /// <summary>Bytes past the header the length field claims: a mismatch makes a bad file.</summary>
    public int LengthField => PayloadLength;

    public int ExtraBytes { get; init; }

    public byte[] Bytes()
    {
        var tag = Cipher == 1 ? 16 : 0;
        var file = new byte[80 + PayloadLength + tag + ExtraBytes];
        var span = file.AsSpan();

        Encoding.ASCII.GetBytes(Magic).CopyTo(span);
        span[4] = Format;
        span[5] = Kind;
        span[6] = Cipher;
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], Version);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)LengthField);
        BinaryPrimitives.WriteInt64LittleEndian(span[16..], Modified);
        RandomNumberGenerator.Fill(span.Slice(24, 12));
        BinaryPrimitives.WriteUInt32LittleEndian(span[36..], KdfIterations);
        RandomNumberGenerator.Fill(span.Slice(40, 16));
        KeyId.CopyTo(span[56..]);
        StoreId.CopyTo(span[64..]);
        SeedId.CopyTo(span[72..]);
        RandomNumberGenerator.Fill(span[80..]);

        return file;
    }
}
