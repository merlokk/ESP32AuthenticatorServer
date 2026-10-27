using AuthServer.Sync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AuthServer.Sync;

/// <summary>The Sync module's registration, and its only public type (architecture.md §3).</summary>
public static class SyncModule
{
    public static IServiceCollection AddSyncModule(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(SyncSettings.SectionName).Get<SyncSettings>() ?? new SyncSettings();

        if (settings.VersionsToKeep < 1 || settings.MaxFileBytes < 96)
        {
            throw new InvalidOperationException("Sync:VersionsToKeep must be at least 1 and Sync:MaxFileBytes at least 96.");
        }

        services.AddSingleton(settings);

        services.AddDbContext<SyncDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("AuthServer"),
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", SyncDbContext.Schema)));

        return services;
    }
}
