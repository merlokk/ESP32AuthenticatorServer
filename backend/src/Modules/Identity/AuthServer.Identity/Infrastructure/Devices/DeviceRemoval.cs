using AuthServer.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Identity.Infrastructure.Devices;

/// <summary>
/// A device goes with its OpenIddict client, in one transaction: it gets no new token (one it holds
/// lapses within the access-token lifetime) and its serial may register again. Its stores stay.
/// </summary>
internal sealed class DeviceRemoval(IdentityDbContext context, DeviceClients clients)
{
    /// <returns>False when the user has no such device.</returns>
    public async Task<bool> RemoveAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        if (await context.Devices.Where(device => device.Id == deviceId && device.UserId == userId).ExecuteDeleteAsync(cancellationToken) == 0)
        {
            return false;
        }

        await clients.DeleteAsync(deviceId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
