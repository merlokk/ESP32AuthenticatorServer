using Microsoft.AspNetCore.Hosting;

namespace AuthServer.IntegrationTests;

internal static class TestHost
{
    public const string PublicOrigin = "http://localhost:3000";

    public const string ClientId = "authserver-web";

    public const string RelyingParty = "localhost";

    public const int SyncVersionsToKeep = 3;

    public const int SyncMaxFileBytes = 4096;

    public static readonly string RedirectUri = $"{PublicOrigin}/auth/callback/authserver";

    public static readonly string TotpEncryptionKey = Convert.ToBase64String(
        System.Text.Encoding.UTF8.GetBytes("integration-tests-totp-key-32-by"));

    public static void Configure(IWebHostBuilder builder, string connectionString)
    {
        // Not Development: the tests migrate the schema themselves, as production does.
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:AuthServer", connectionString);
        builder.UseSetting("Identity:PublicOrigin", PublicOrigin);
        builder.UseSetting("Identity:ClientId", ClientId);
        builder.UseSetting("Identity:TotpEncryptionKey", TotpEncryptionKey);

        // Short enough that expiry is testable, long enough for a slow run (plus 5 s skew).
        builder.UseSetting("Identity:AccessTokenLifetime", "00:00:02");
        builder.UseSetting("Identity:RefreshTokenLifetime", "01:00:00");

        // Few enough that pruning is testable.
        builder.UseSetting("Sync:VersionsToKeep", SyncVersionsToKeep.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.UseSetting("Sync:MaxFileBytes", SyncMaxFileBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
