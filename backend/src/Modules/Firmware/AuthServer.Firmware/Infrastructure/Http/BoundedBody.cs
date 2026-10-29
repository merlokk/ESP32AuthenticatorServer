namespace AuthServer.Firmware.Infrastructure.Http;

/// <summary>An image read whole, or refused past the limit: no further than one chunk over it.</summary>
internal static class BoundedBody
{
    /// <returns>The bytes, or null past <paramref name="maxBytes"/>.</returns>
    public static async Task<byte[]?> ReadAsync(Stream body, long? length, int maxBytes, CancellationToken cancellationToken)
    {
        if (length > maxBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;

        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);

            if (buffer.Length > maxBytes)
            {
                return null;
            }
        }

        return buffer.ToArray();
    }
}
