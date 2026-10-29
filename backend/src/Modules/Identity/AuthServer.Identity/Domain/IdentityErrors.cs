using AuthServer.Shared;

namespace AuthServer.Identity.Domain;

/// <summary>The module's expected failures. Codes are public API (Problem Details <c>type</c>).</summary>
internal static class IdentityErrors
{
    // One refusal for every cause: no account, no or expired window, blocked, bad signature.
    public static readonly Error RegistrationRefused = new("device-registration.refused", "The device cannot be registered.");

    public static readonly Error RegistrationInvalid = new("device-registration.invalid", "The registration is malformed.");

    public static readonly Error RegistrationStale = new("device-registration.stale", "The registration's iat is more than 10 minutes off: check the device clock.");

    public static readonly Error DeviceNotFound = new("device.not-found", "There is no such device.");

    public static readonly Error DeviceTaken = new("device.taken", "This device is registered to another account.");

    // One refusal for every cause: no account, wrong or reused code, lockout, blocked.
    public static readonly Error SignInRefused = new("sign-in.refused", "That did not work. Check the address and the code.");

    public static readonly Error UserNotFound = new("user.not-found", "There is no such user.");

    public static readonly Error UserNameInvalid = new("user.name-invalid", "A user name is 1 to 64 letters, digits, '.', '_' or '-'.");

    public static readonly Error EmailInvalid = new("user.email-invalid", "That is not an e-mail address.");

    public static readonly Error UserNameTaken = new("user.name-taken", "That user name is taken.");

    public static readonly Error EmailTaken = new("user.email-taken", "That e-mail address is taken.");

    public static readonly Error RoleInvalid = new("user.role-invalid", "The role is 'user' or 'admin'.");

    public static readonly Error LastAdmin = new("admin.last", "This would leave no active admin.");

    public static readonly Error DeviceStatusInvalid = new("device-status.invalid", "A status report is a JSON object of at most 4 KB with firmware (1 to 32 characters).");

    public static readonly Error RegistrationWindowInvalid = new("registration-window.invalid", "A registration window is 1 to 24 hours.");

    public static readonly Error PasskeyNameInvalid = new("passkey.name-invalid", "A passkey name is 1 to 64 characters.");

    public static readonly Error PasskeyLimitReached = new("passkey.limit-reached", "An account holds at most 10 passkeys.");

    public static readonly Error PasskeyAlreadyRegistered = new("passkey.already-registered", "This passkey is already registered.");

    public static readonly Error PasskeyRegistrationFailed = new("passkey.registration-failed", "The passkey could not be registered.");

    public static readonly Error PasskeyNotFound = new("passkey.not-found", "There is no such passkey.");
}
