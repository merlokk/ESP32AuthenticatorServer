using AuthServer.Firmware.Contracts;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using AuthServer.Firmware.Features.OfferFirmware;
using AuthServer.Firmware.Infrastructure.GitHub;
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

        if (settings.GitHubRepository.Length > 0 && !Regex.IsMatch(settings.GitHubRepository, "^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$"))
        {
            throw new InvalidOperationException("Firmware:GitHubRepository must be owner/name, or empty.");
        }

        services.AddSingleton(settings);

        // Anonymous: public releases only, 60 requests an hour per address (an admin's clicks).
        services.AddHttpClient<GitHubReleases>(client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.Timeout = TimeSpan.FromMinutes(1);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ESP32AuthenticatorServer", null));
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        });

        services.AddDbContext<FirmwareDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("AuthServer"),
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", FirmwareDbContext.Schema)));

        services.AddScoped<IFirmwareOffers, Offers>();

        return services;
    }
}
