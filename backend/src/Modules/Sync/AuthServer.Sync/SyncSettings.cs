namespace AuthServer.Sync;

/// <summary>The <c>Sync</c> configuration section, bound once (architecture.md §7).</summary>
internal sealed class SyncSettings
{
    public const string SectionName = "Sync";

    /// <summary>Versions kept per store; an accepted upload deletes the ones beyond.</summary>
    public int VersionsToKeep { get; set; } = 20;

    /// <summary>The largest store file accepted, header and tag included.</summary>
    public int MaxFileBytes { get; set; } = 256 * 1024;
}
