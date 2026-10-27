namespace AuthServer.Firmware.Domain;

/// <summary>
/// One uploaded image for one board. At most one release per board is <see cref="Active"/>: the one
/// offered to that board's devices; switching it is how a version is rolled out or back.
/// </summary>
internal sealed class FirmwareRelease
{
    public Guid Id { get; init; }

    /// <summary>A <see cref="Boards"/> id.</summary>
    public string Board { get; init; } = string.Empty;

    /// <summary>esp_app_desc_t.version, what the device reports when it runs it.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>esp_app_desc_t date and time: "Oct 27 2026 21:07:30".</summary>
    public string Built { get; init; } = string.Empty;

    /// <summary>esp_app_desc_t.idf_ver.</summary>
    public string Idf { get; init; } = string.Empty;

    public long Size { get; init; }

    /// <summary>Of the whole file, lower-case hex.</summary>
    public string Sha256 { get; init; } = string.Empty;

    public bool Active { get; set; }

    public DateTimeOffset UploadedAt { get; init; }

    /// <summary>The admin who uploaded it.</summary>
    public Guid UploadedBy { get; init; }

    public byte[] Content { get; init; } = [];
}
