using System.Net.Mail;
using AuthServer.Identity.Contracts;
using AuthServer.Shared;

namespace AuthServer.Identity.Domain;

internal static class UserRoles
{
    public const string Admin = IdentityRoles.Admin;

    public const string User = IdentityRoles.User;

    public static string? Of(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        Admin => Admin,
        User => User,
        _ => null,
    };
}

internal static class AccountFields
{
    public const int MaximumUserNameLength = 64;

    public const int MaximumEmailLength = 256;

    public const int MaximumPasskeyNameLength = 64;

    public const int MaximumPasskeys = 10;

    public const int MaximumDeviceNameLength = 64;

    public static Result<string> UserNameOf(string? value)
    {
        var name = (value ?? string.Empty).Trim();

        return name.Length is > 0 and <= MaximumUserNameLength && name.All(IsUserNameCharacter)
            ? name
            : IdentityErrors.UserNameInvalid;
    }

    public static Result<string> EmailOf(string? value)
    {
        var email = (value ?? string.Empty).Trim();

        // A bare address only: MailAddress also accepts "Name <a@b.c>".
        return email.Length is > 0 and <= MaximumEmailLength
            && !email.Any(char.IsControl)
            && MailAddress.TryCreate(email, out var parsed)
            && parsed.Address == email
            && parsed.DisplayName.Length == 0
                ? email
                : IdentityErrors.EmailInvalid;
    }

    /// <summary>The local part of the address, mapped into the user-name alphabet.</summary>
    public static string DefaultUserNameFor(string email)
    {
        var local = email.Split('@')[0];
        var name = new string([.. local.Select(character => IsUserNameCharacter(character) ? character : '-')]).Trim('-');

        return name.Length == 0 ? "user" : name[..Math.Min(name.Length, MaximumUserNameLength)];
    }

    public static Result<string> PasskeyNameOf(string? value)
    {
        var name = (value ?? string.Empty).Trim();

        return name.Length is > 0 and <= MaximumPasskeyNameLength && !name.Any(char.IsControl)
            ? name
            : IdentityErrors.PasskeyNameInvalid;
    }

    /// <summary>A device's name, trimmed; empty clears it (null).</summary>
    public static Result<string?> DeviceNameOf(string? value)
    {
        var name = (value ?? string.Empty).Trim();

        if (name.Length > MaximumDeviceNameLength || name.Any(char.IsControl))
        {
            return IdentityErrors.DeviceNameInvalid;
        }

        return name.Length == 0 ? null : name;
    }

    private static bool IsUserNameCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-';
}

internal static class RegistrationWindow
{
    /// <summary>
    /// A user without a device is being invited (the long window); one with a device is adding
    /// another (the short one). Opening again extends from now.
    /// </summary>
    public static DateTimeOffset Until(DateTimeOffset now, bool hasDevices, TimeSpan inviteWindow, TimeSpan deviceWindow) =>
        now + (hasDevices ? deviceWindow : inviteWindow);

    public static bool IsOpen(DateTimeOffset? until, DateTimeOffset now) => until is { } end && now < end;

    public const int MaximumHours = 24;

    /// <summary>A length an admin chose: 1 to 24 whole hours.</summary>
    public static Result<TimeSpan> HoursOf(int hours) =>
        hours is >= 1 and <= MaximumHours ? TimeSpan.FromHours(hours) : IdentityErrors.RegistrationWindowInvalid;
}

internal static class LastAdminRule
{
    /// <summary>A change refused because no active (unblocked) admin would remain.</summary>
    public static bool Refuses(bool targetIsActiveAdmin, int activeAdmins, bool targetStaysActiveAdmin) =>
        targetIsActiveAdmin && !targetStaysActiveAdmin && activeAdmins <= 1;
}
