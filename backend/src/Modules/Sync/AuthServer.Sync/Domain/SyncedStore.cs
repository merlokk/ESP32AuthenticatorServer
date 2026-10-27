namespace AuthServer.Sync.Domain;

/// <summary>
/// One store of one user as the server keeps it: copies of a store on synced devices share the
/// store id, and only devices with the same seed id can open each other's copies, so the seed id is
/// part of the key. <see cref="HeadVersion"/> is the newest version held.
/// </summary>
internal sealed class SyncedStore
{
    public Guid Id { get; init; }

    /// <summary>The owner, the device token's sub.</summary>
    public Guid UserId { get; init; }

    public string StoreId { get; init; } = string.Empty;

    public string SeedId { get; init; } = string.Empty;

    public StoreKind Kind { get; init; }

    public long HeadVersion { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One accepted copy: the header fields the UI shows and the whole file, never opened.</summary>
internal sealed class StoreVersion
{
    public Guid Id { get; init; }

    public Guid StoreRef { get; init; }

    /// <summary>The header's uint32 counter.</summary>
    public long Version { get; init; }

    /// <summary>Unix seconds by the device's clock; 0 = unknown.</summary>
    public long Modified { get; init; }

    public StoreCipher Cipher { get; init; }

    public long Length { get; init; }

    public long KdfIterations { get; init; }

    public string KeyId { get; init; } = string.Empty;

    /// <summary>The device_id of the token that uploaded it (an Identity device; may be gone since).</summary>
    public Guid UploadedByDevice { get; init; }

    public DateTimeOffset UploadedAt { get; init; }

    public byte[] Content { get; init; } = [];
}
