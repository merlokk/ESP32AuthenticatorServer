using AuthServer.Firmware.Contracts;
using AuthServer.Firmware.Features.OfferFirmware;
using AuthServer.Firmware.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AuthServer.Firmware;

/// <summary>The Firmware module's registration, and its only public type (architecture.md §3).</summary>
public static class FirmwareModule
{
    public static IServiceCollection AddFirmwareModule(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(FirmwareSettings.SectionName).Get<FirmwareSettings>() ?? new FirmwareSettings();

        if (string.IsNullOrWhiteSpace(settings.Project) || settings.MaxImageBytes < 1024)
        {
            throw new InvalidOperationException("Firmware:Project must be set and Firmware:MaxImageBytes at least 1024.");
        }

        services.AddSingleton(settings);

        services.AddDbContext<FirmwareDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("AuthServer"),
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", FirmwareDbContext.Schema)));

        services.AddScoped<IFirmwareOffers, Offers>();

        return services;
    }
}
