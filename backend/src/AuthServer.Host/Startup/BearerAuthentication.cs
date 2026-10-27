using AuthServer.Identity.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace AuthServer.Host.Startup;

/// <summary>
/// The one bearer scheme every module's endpoints read, and the policies they name. Identity hands
/// it the signing key in-process (no discovery request to itself).
/// </summary>
internal static class BearerAuthentication
{
    public static IServiceCollection AddBearerAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var issuer = (configuration["Identity:PublicOrigin"] ?? string.Empty).TrimEnd('/');

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Claims keep their token names: sub, name, role, device_id.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuers = [issuer, issuer + "/"],
                    ValidAudience = IdentityResources.Api,
                    ClockSkew = TimeSpan.FromSeconds(5),
                    NameClaimType = IdentityClaims.Name,
                    RoleClaimType = IdentityClaims.Role,
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(IdentityPolicies.Account, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => !context.User.HasClaim(claim => claim.Type == IdentityClaims.DeviceId)))
            .AddPolicy(IdentityPolicies.Admin, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(IdentityRoles.Admin)
                .RequireAssertion(context => !context.User.HasClaim(claim => claim.Type == IdentityClaims.DeviceId)))
            .AddPolicy(IdentityPolicies.Device, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(IdentityClaims.DeviceId));

        return services;
    }
}
