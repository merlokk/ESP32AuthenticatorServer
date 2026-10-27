using AuthServer.Shared;

namespace AuthServer.Firmware.Domain;

/// <summary>Firmware's expected failures; the code is the Problem Details type, public API.</summary>
internal static class FirmwareErrors
{
    public static readonly Error ImageInvalid = new(
        "firmware.image-invalid",
        "That is not an image of this firmware for an ESP32-S3 (header, segments, checksum, SHA-256, project, version).");

    public static readonly Error ImageTooLarge = new("firmware.image-too-large", "The image is larger than the OTA slot.");

    public static readonly Error BoardUnknown = new("firmware.board-unknown", "There is no such board.");

    public static readonly Error ReleaseExists = new("firmware.release-exists", "This board already has a release of that version.");

    public static readonly Error ReleaseNotFound = new("firmware.release-not-found", "There is no such release.");
}
