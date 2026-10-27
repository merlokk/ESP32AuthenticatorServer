using AuthServer.Firmware.Contracts;
using AuthServer.Firmware.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuthServer.Firmware.Features.OfferFirmware;

/// <summary>
/// The board's active release, for Identity's status answer. Versions are compared as strings,
/// never ordered: the admin's choice of the active release decides, so an older one rolls back.
/// </summary>
internal sealed class Offers(FirmwareDbContext context) : IFirmwareOffers
{
    public async Task<FirmwareOffer?> OfferAsync(string board, string runningVersion, CancellationToken cancellationToken)
    {
        var active = await context.Releases
            .Where(release => release.Board == board && release.Active)
            .Select(release => new FirmwareOffer(release.Id, release.Version, release.Size, release.Sha256))
            .SingleOrDefaultAsync(cancellationToken);

        return active is null || active.Version == runningVersion ? null : active;
    }
}
