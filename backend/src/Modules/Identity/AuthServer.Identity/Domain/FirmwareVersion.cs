using System.Text.RegularExpressions;

namespace AuthServer.Identity.Domain;

/// <summary>
/// The firmware's version is git's (firmware cmake/version.cmake): x.y.z-&lt;commits&gt;-g&lt;hash&gt;[-dirty],
/// or x.y.z-nogit; a release is x.y.z. Only the release part is compared: a build after a release
/// is that release or later, never behind it.
/// </summary>
internal static partial class FirmwareVersion
{
    public const int MaximumLength = 32;

    public static bool IsValid(string? reported) =>
        reported is { Length: > 0 and <= MaximumLength } && reported.All(character => character is > ' ' and <= '~');

    /// <summary>The configured release if it is above the reported version; else null.</summary>
    public static string? Update(string reported, string? latest)
    {
        if (Release(latest) is not { } release)
        {
            return null;
        }

        // A version the server cannot read is offered the release: the device decides.
        return Release(reported) is { } current && current >= release ? null : latest!.Trim();
    }

    private static Version? Release(string? value) =>
        value is not null && ReleasePart().Match(value.Trim()) is { Success: true } match
            ? new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value))
            : null;

    [GeneratedRegex(@"^(\d{1,5})\.(\d{1,5})\.(\d{1,5})(?:-|$)")]
    private static partial Regex ReleasePart();
}
