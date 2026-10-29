namespace AuthServer.Firmware;

/// <summary>The <c>Firmware</c> configuration section, bound once (architecture.md §7).</summary>
internal sealed class FirmwareSettings
{
    public const string SectionName = "Firmware";

    /// <summary>esp_app_desc_t.project_name of the firmware (its CMake project): another is refused.</summary>
    public string Project { get; set; } = "esp32-authenticator";

    /// <summary>The largest image accepted: the OTA slot (app0/app1, 0x7E0000 on the X4 Pro layout).</summary>
    public int MaxImageBytes { get; set; } = 0x7E0000;

    /// <summary>"owner/name" of the GitHub repository whose release images an admin imports; empty: no import.</summary>
    public string GitHubRepository { get; set; } = string.Empty;
}
