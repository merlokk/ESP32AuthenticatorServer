using AuthServer.Identity.Contracts;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AuthServer.Identity.Features.GetConfiguration;

internal sealed class Endpoint(IdentitySettings settings) : EndpointWithoutRequest<ConfigurationResponse>
{
    public override void Configure()
    {
        Get("/identity/configuration");
        Policies(IdentityPolicies.Admin);
        Options(route => route.WithTags("Identity").WithName("GetConfiguration"));
        Summary(summary =>
        {
            summary.Summary = "The Identity configuration the server runs with (admin).";
            summary.Description = "Read-only: it comes from appsettings and the environment. Durations in seconds; a secret only as set or not.";
        });
    }

    public override Task HandleAsync(CancellationToken cancellationToken) =>
        Send.OkAsync(
            new ConfigurationResponse(
                settings.PublicOrigin,
                settings.ClientId,
                (long)settings.AccessTokenLifetime.TotalSeconds,
                (long)settings.RefreshTokenLifetime.TotalSeconds,
                (long)settings.InviteWindow.TotalSeconds,
                (long)settings.DeviceWindow.TotalSeconds,
                settings.TotpEncryptionKey.Length > 0,
                settings.LatestFirmware),
            cancellationToken);
}

/// <param name="TotpEncryptionKeySet">Whether the key is configured; the key itself never leaves the server.</param>
internal sealed record ConfigurationResponse(
    string PublicOrigin,
    string ClientId,
    long AccessTokenLifetimeSeconds,
    long RefreshTokenLifetimeSeconds,
    long InviteWindowSeconds,
    long DeviceWindowSeconds,
    bool TotpEncryptionKeySet,
    string LatestFirmware);
