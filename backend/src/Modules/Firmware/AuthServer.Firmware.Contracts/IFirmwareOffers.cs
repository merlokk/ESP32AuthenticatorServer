namespace AuthServer.Firmware.Contracts;

/// <summary>Which release a device should run: the Firmware module answers, Identity asks in the status call.</summary>
public interface IFirmwareOffers
{
    /// <summary>
    /// The board's active release when the device runs another version (older or newer: activating
    /// an older release rolls devices back); null when it runs it, or the board has none.
    /// </summary>
    Task<FirmwareOffer?> OfferAsync(string board, string runningVersion, CancellationToken cancellationToken);
}

/// <param name="Id">GET /api/device/firmware/{id}/image fetches it.</param>
/// <param name="Size">Bytes of the image.</param>
/// <param name="Sha256">Of the whole image, lower-case hex.</param>
public sealed record FirmwareOffer(Guid Id, string Version, long Size, string Sha256);
