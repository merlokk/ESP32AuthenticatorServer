using System.Buffers.Binary;
using AuthServer.Shared;

namespace AuthServer.Sync.Domain;

/// <summary>Which store a file is (firmware storefile::Kind): a file of one kind is never another.</summary>
internal enum StoreKind : byte
{
    Fido = 1,
    Records = 2,
}

internal enum StoreCipher : byte
{
    Plain = 0,
    AesGcm = 1,
}

/// <summary>
/// The plain 80-byte header in front of every store file (firmware store_header.h), the only part
/// the server reads. Little-endian: magic "ESAF", format 3, kind, cipher, reserved, version (4),
/// length (4), modified (8, Unix seconds, 0 = unknown), nonce (12), KDF iterations (4), salt (16),
/// key id (8), store id (8), seed id (4), reserved (4); then `length` payload bytes and a 16-byte
/// GCM tag. The header is the GCM associated data: the server cannot check it, the device does.
/// </summary>
internal sealed record StoreHeader(
    StoreKind Kind,
    StoreCipher Cipher,
    uint Version,
    uint Length,
    long Modified,
    uint KdfIterations,
    string KeyId,
    string StoreId,
    string SeedId,
    string Nonce)
{
    public const int Size = 80;
    public const int TagSize = 16;
    public const byte Format = 3;

    private static ReadOnlySpan<byte> Magic => "ESAF"u8;

    public static Result<StoreHeader> Parse(ReadOnlySpan<byte> file)
    {
        if (file.Length < Size || !file[..4].SequenceEqual(Magic) || file[4] != Format)
        {
            return SyncErrors.FileInvalid;
        }

        var kind = (StoreKind)file[5];
        var cipher = (StoreCipher)file[6];

        if (!Enum.IsDefined(kind) || !Enum.IsDefined(cipher))
        {
            return SyncErrors.FileInvalid;
        }

        if (cipher == StoreCipher.Plain)
        {
            return SyncErrors.FilePlain;
        }

        var length = BinaryPrimitives.ReadUInt32LittleEndian(file[12..]);

        if ((long)file.Length != Size + (long)length + TagSize)
        {
            return SyncErrors.FileInvalid;
        }

        return new StoreHeader(
            kind,
            cipher,
            Version: BinaryPrimitives.ReadUInt32LittleEndian(file[8..]),
            length,
            Modified: BinaryPrimitives.ReadInt64LittleEndian(file[16..]),
            KdfIterations: BinaryPrimitives.ReadUInt32LittleEndian(file[36..]),
            KeyId: Hex(file.Slice(56, 8)),
            StoreId: Hex(file.Slice(64, 8)),
            SeedId: Hex(file.Slice(72, 4)),
            Nonce: Hex(file.Slice(24, 12)));
    }

    private static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(bytes);
}

/// <summary>
/// The order is the counter's (the version, +1 on every write): the time is shown, never compared,
/// since device clocks drift. The device that wants to win writes max(both) + 1.
/// </summary>
internal static class UploadRule
{
    /// <summary>A copy is kept only above the newest the server holds.</summary>
    public static bool Accepts(uint upload, uint? head) => head is not { } current || upload > current;

    /// <summary>Never a version at or below what the device already has (rollback).</summary>
    public static bool Serves(uint head, uint? above) => above is not { } held || head > held;
}
