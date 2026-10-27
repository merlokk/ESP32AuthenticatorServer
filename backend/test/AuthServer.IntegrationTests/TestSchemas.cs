using AuthServer.Firmware.Infrastructure.Persistence;
using AuthServer.Identity.Infrastructure.Persistence;
using AuthServer.Sync.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuthServer.IntegrationTests;

// The schema as the migrations job builds it: every module's migrations, nothing at startup.
internal static class TestSchemas
{
    public static async Task MigrateAsync(string connectionString)
    {
        // What the identity model reads from the host: schema version 3 (passkeys).
        await using var services = new ServiceCollection()
            .Configure<IdentityOptions>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .BuildServiceProvider();

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseApplicationServiceProvider(services)
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema));
        options.UseOpenIddict<Guid>();

        await using var identity = new IdentityDbContext(options.Options);
        await identity.Database.MigrateAsync();

        await using var sync = new SyncDbContext(new DbContextOptionsBuilder<SyncDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", SyncDbContext.Schema))
            .Options);
        await sync.Database.MigrateAsync();

        await using var firmware = new FirmwareDbContext(new DbContextOptionsBuilder<FirmwareDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", FirmwareDbContext.Schema))
            .Options);
        await firmware.Database.MigrateAsync();
    }
}
