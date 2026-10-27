using AuthServer.Identity.Infrastructure.Devices;
using AuthServer.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthServer.Identity.Infrastructure.Startup;

/// <summary>
/// Runs at start: refuse a bad configuration, seed the BFF client. Never migrates: the
/// migrations job does, before the host starts (architecture.md §6). A hosted service, so an
/// efbundle (which builds the container, never starts it) runs none of it.
/// </summary>
internal sealed class IdentityStartup(
    IServiceProvider services,
    IdentitySettings settings,
    ILogger<IdentityStartup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        CheckSettings();

        await using var scope = services.CreateAsyncScope();

        await SeedClientAsync(scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>(), cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void CheckSettings()
    {
        if (!Uri.TryCreate(settings.PublicOrigin, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                "Identity:PublicOrigin is not an absolute URL. It is the origin the browser uses (http://localhost:3000 "
                + "locally): the token issuer and the WebAuthn relying party. It is never inferred from a request.");
        }

        _ = new TotpSecretProtector(settings.TotpEncryptionKey);
    }

    private async Task SeedClientAsync(IOpenIddictApplicationManager applications, CancellationToken cancellationToken)
    {
        var origin = settings.TrimmedPublicOrigin;

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = settings.ClientId,
            ClientType = ClientTypes.Public,
            ApplicationType = ApplicationTypes.Web,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "AuthServer",
            RedirectUris = { new Uri($"{origin}/auth/callback/authserver") },
            PostLogoutRedirectUris = { new Uri($"{origin}/") },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.Endpoints.Revocation,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Email,
                Permissions.Scopes.Roles,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };

        if (await applications.FindByClientIdAsync(settings.ClientId, cancellationToken) is { } existing)
        {
            await applications.UpdateAsync(existing, descriptor, cancellationToken);
        }
        else
        {
            await applications.CreateAsync(descriptor, cancellationToken);
            logger.LogInformation("Created the OpenIddict client {ClientId}.", settings.ClientId);
        }
    }
}
