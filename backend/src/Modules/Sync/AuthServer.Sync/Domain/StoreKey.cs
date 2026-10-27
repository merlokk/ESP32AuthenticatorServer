using AuthServer.Shared;

namespace AuthServer.Sync.Domain;

/// <summary>A store as a device names it: store id (8 bytes) and seed id (4 bytes) in hex, and the kind.</summary>
internal readonly record struct StoreKey(string StoreId, string SeedId, StoreKind Kind)
{
    public static Result<StoreKey> Create(string? storeId, string? seedId, int kind) =>
        IsHex(storeId, 16) && IsHex(seedId, 8) && Enum.IsDefined((StoreKind)kind)
            ? new StoreKey(storeId!.ToLowerInvariant(), seedId!.ToLowerInvariant(), (StoreKind)kind)
            : SyncErrors.RequestInvalid;

    public static StoreKey Of(StoreHeader header) => new(header.StoreId, header.SeedId, header.Kind);

    private static bool IsHex(string? value, int length) =>
        value is not null && value.Length == length && value.All(char.IsAsciiHexDigit);
}
