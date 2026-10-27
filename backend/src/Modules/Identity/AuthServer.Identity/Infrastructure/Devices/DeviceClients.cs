using AuthServer.Identity.Domain;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthServer.Identity.Infrastructure.Devices;

/// <summary>
/// Each device is an OpenIddict client: its id is the client id, its public key the client's
/// JSON Web Key Set. It authenticates with private_key_jwt and may only use client_credentials.
/// </summary>
internal sealed class DeviceClients(IOpenIddictApplicationManager applications)
{
    public async Task UpsertAsync(Device device, CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = device.Id.ToString(),
            ClientType = ClientTypes.Confidential,
            DisplayName = $"Device {device.HardwareId}",
            JsonWebKeySet = new JsonWebKeySet { Keys = { KeyOf(device.PublicKey) } },
            Permissions =
            {
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.ClientCredentials,
            },
        };

        if (await applications.FindByClientIdAsync(descriptor.ClientId, cancellationToken) is { } existing)
        {
            await applications.UpdateAsync(existing, descriptor, cancellationToken);
        }
        else
        {
            await applications.CreateAsync(descriptor, cancellationToken);
        }
    }

    public async Task DeleteAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        if (await applications.FindByClientIdAsync(deviceId.ToString(), cancellationToken) is { } existing)
        {
            await applications.DeleteAsync(existing, cancellationToken);
        }
    }

    // 0x04 || X || Y → an EC P-256 JWK.
    private static JsonWebKey KeyOf(byte[] point) => new()
    {
        Kty = JsonWebAlgorithmsKeyTypes.EllipticCurve,
        Crv = JsonWebKeyECTypes.P256,
        X = Base64UrlEncoder.Encode(point[1..33]),
        Y = Base64UrlEncoder.Encode(point[33..]),
        Use = JsonWebKeyUseNames.Sig,
        Alg = SecurityAlgorithms.EcdsaSha256,
    };
}
