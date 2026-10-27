using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AuthServer.Identity.Infrastructure.Tokens;

/// <summary>
/// The access-token signing pair: ES256, generated at every start, persisted nowhere. Access
/// tokens die with the process (they live minutes); refresh tokens and codes are Data Protection
/// tokens and survive. Two replicas would refuse each other's access tokens: one replica until
/// this is persisted.
/// </summary>
internal sealed class SigningKey
{
    public ECDsaSecurityKey Key { get; } = new(ECDsa.Create(ECCurve.NamedCurves.nistP256))
    {
        KeyId = Guid.CreateVersion7().ToString("N"),
    };
}

/// <summary>
/// Hands the host's JwtBearer this process's key and issuer directly: no discovery request from
/// the backend to itself (behind the ingress, its public origin is the BFF).
/// </summary>
internal sealed class LocalBearerConfiguration(SigningKey signingKey, IdentitySettings settings)
    : IPostConfigureOptions<JwtBearerOptions>
{
    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        var configuration = new OpenIdConnectConfiguration { Issuer = settings.TrimmedPublicOrigin + "/" };
        configuration.SigningKeys.Add(signingKey.Key);

        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
    }
}
