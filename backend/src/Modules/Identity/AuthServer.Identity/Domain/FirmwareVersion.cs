namespace AuthServer.Identity.Domain;

/// <summary>
/// What a device reports of its firmware: the version is git's (firmware cmake/version.cmake),
/// x.y.z-&lt;commits&gt;-g&lt;hash&gt;[-dirty] or x.y.z-nogit, esp_app_desc_t.version; the board is the
/// firmware's build target (x4pro, dev). Kept as reported, never parsed.
/// </summary>
internal static class FirmwareVersion
{
    public const int MaximumLength = 32;

    public const int MaximumBoardLength = 16;

    public static bool IsValid(string? reported) => IsPrintable(reported, MaximumLength);

    public static bool IsValidBoard(string? board) => IsPrintable(board, MaximumBoardLength);

    private static bool IsPrintable(string? value, int maximum) =>
        value is { Length: > 0 } && value.Length <= maximum && value.All(character => character is > ' ' and <= '~');
}
