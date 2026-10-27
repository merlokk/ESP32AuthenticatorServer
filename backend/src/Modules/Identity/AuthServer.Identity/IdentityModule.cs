using AuthServer.Identity.Contracts;
using AuthServer.Identity.Domain;
using AuthServer.Identity.Infrastructure.Accounts;
using AuthServer.Identity.Infrastructure.Devices;
using AuthServer.Identity.Infrastructure.Persistence;
using AuthServer.Identity.Infrastructure.Startup;
using AuthServer.Identity.Infrastructure.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace AuthServer.Identity;

/// <summary>The Identity module's registration, and its only public type (architecture.md §3).</summary>
public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(IdentitySettings.SectionName).Get<IdentitySettings>() ?? new IdentitySettings();
        services.AddSingleton(settings);

        services.AddDbContext<IdentityDbContext>(options => options
            .UseNpgsql(
                configuration.GetConnectionString("AuthServer"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema))
            .UseOpenIddict<Guid>());

        // Cookies, refresh tokens and authorization codes outlive a restart.
        services.AddDataProtection()
            .SetApplicationName("AuthServer")
            .PersistKeysToDbContext<IdentityDbContext>();

        AddAccounts(services, settings);
        AddAuthorizationServer(services, settings);

        // Built on first use, so a bad key fails the start (IdentityStartup), not an efbundle.
        services.AddSingleton(_ => new TotpSecretProtector(settings.TotpEncryptionKey));
        services.AddScoped<DeviceClients>();
        services.AddScoped<AccountChanges>();

        services.AddSingleton<SigningKey>();
        services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>, LocalBearerConfiguration>();

        services.AddHostedService<IdentityStartup>();

        return services;
    }

    private static void AddAccounts(IServiceCollection services, IdentitySettings settings)
    {
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
                options.User.RequireUniqueEmail = true;
                options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-";

                // Wrong TOTP codes count; five lock the account for a quarter of an hour.
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                // The token's claim names, so a bearer principal and the cookie read alike.
                options.ClaimsIdentity.UserIdClaimType = IdentityClaims.Subject;
                options.ClaimsIdentity.UserNameClaimType = IdentityClaims.Name;
                options.ClaimsIdentity.EmailClaimType = IdentityClaims.Email;
                options.ClaimsIdentity.RoleClaimType = IdentityClaims.Role;
            })
            .AddUserStore<AccountStore>()
            .AddSignInManager<AccountSignInManager>();

        services.Configure<IdentityPasskeyOptions>(options =>
        {
            // The relying party is the public origin, stated: behind a proxy the request's own
            // origin is wrong, and every passkey is bound to this host forever.
            var origin = settings.TrimmedPublicOrigin;
            options.ServerDomain = Uri.TryCreate(origin, UriKind.Absolute, out var parsed) ? parsed.Host : null;
            options.ValidateOrigin = context => ValueTask.FromResult(string.Equals(context.Origin, origin, StringComparison.OrdinalIgnoreCase));
        });

        // Identity's own cookie: the browser's proof to the authorization server, nothing else.
        // The module never sets the default scheme; the host does (JwtBearer).
        services.AddAuthentication()
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.Cookie.HttpOnly = true;
                // Lax: the authorization-code flow comes back by a top-level redirect.
                options.Cookie.SameSite = SameSiteMode.Lax;
                // http on localhost in development.
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.SlidingExpiration = false;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                // The backend has no pages: a status code, never a redirect.
                options.Events.OnRedirectToLogin = context => Refuse(context.Response, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = context => Refuse(context.Response, StatusCodes.Status403Forbidden);
            })
            // The passkey ceremony's state between its two calls lives in this scheme's cookie.
            .AddCookie(IdentityConstants.TwoFactorUserIdScheme, options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
                options.Events.OnRedirectToLogin = context => Refuse(context.Response, StatusCodes.Status401Unauthorized);
            });
    }

    private static void AddAuthorizationServer(IServiceCollection services, IdentitySettings settings)
    {
        services.AddOpenIddict()
            .AddCore(options => options
                .UseEntityFrameworkCore()
                .UseDbContext<IdentityDbContext>()
                .ReplaceDefaultEntities<Guid>())
            .AddServer(options =>
            {
                options
                    .SetAuthorizationEndpointUris("connect/authorize")
                    .SetTokenEndpointUris("connect/token")
                    .SetUserInfoEndpointUris("connect/userinfo")
                    .SetEndSessionEndpointUris("connect/endsession")
                    .SetRevocationEndpointUris("connect/revocation");

                options
                    .AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange()
                    .AllowRefreshTokenFlow()
                    .AllowClientCredentialsFlow();

                if (Uri.TryCreate(settings.PublicOrigin, UriKind.Absolute, out var issuer))
                {
                    // The browser's origin, not the container's: it is in every token.
                    options.SetIssuer(issuer);
                }

                options.RegisterScopes(
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Scopes.Email,
                    OpenIddictConstants.Scopes.Roles,
                    OpenIddictConstants.Scopes.OfflineAccess);

                options.SetAccessTokenLifetime(settings.AccessTokenLifetime);
                options.SetRefreshTokenLifetime(settings.RefreshTokenLifetime);

                // Not rolling: concurrent renewals in the BFF all succeed (architecture.md §9).
                options.DisableRollingRefreshTokens();

                // The BFF's cookie holds a short reference, the refresh token stays in the table.
                options.UseReferenceRefreshTokens();

                options.DisableAccessTokenEncryption();

                // Access tokens: JWTs signed with the per-process key, read by JwtBearer. Refresh
                // tokens and codes: Data Protection, so they survive a restart. The encryption key
                // is ephemeral because no token this server issues is encrypted with it.
                options.AddEphemeralEncryptionKey();
                options.UseDataProtection().PreferDefaultAccessTokenFormat();

                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    // TLS ends at the ingress; the backend speaks http inside the cluster.
                    .DisableTransportSecurityRequirement();
            });

        // The signing key is the one LocalBearerConfiguration hands JwtBearer.
        services.AddOptions<OpenIddict.Server.OpenIddictServerOptions>()
            .Configure<SigningKey>((options, signingKey) => options.SigningCredentials.Add(
                new Microsoft.IdentityModel.Tokens.SigningCredentials(signingKey.Key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.EcdsaSha256)));
    }

    private static Task Refuse(HttpResponse response, int statusCode)
    {
        response.StatusCode = statusCode;
        return Task.CompletedTask;
    }
}
