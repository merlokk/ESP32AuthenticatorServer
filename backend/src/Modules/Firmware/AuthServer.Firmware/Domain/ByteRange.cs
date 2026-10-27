using System.Globalization;

namespace AuthServer.Firmware.Domain;

/// <summary>
/// The Range header of an OTA download (RFC 9110 §14): one byte range, first-last, first- or
/// -suffix. Anything else (no header, another unit, several ranges, garbage) is the whole file, as
/// the RFC lets a server do; a range that starts past the end is unsatisfiable (416).
/// </summary>
internal static class ByteRange
{
    public enum Kind
    {
        Whole,
        Partial,
        Unsatisfiable,
    }

    public sealed record Outcome(Kind Kind, long Offset, long Length);

    public static Outcome Parse(string? header, long size)
    {
        var whole = new Outcome(Kind.Whole, 0, size);
        var unsatisfiable = new Outcome(Kind.Unsatisfiable, 0, 0);

        if (string.IsNullOrWhiteSpace(header))
        {
            return whole;
        }

        var equals = header.IndexOf('=', StringComparison.Ordinal);

        if (equals < 0 || header[..equals].Trim() != "bytes")
        {
            return whole;
        }

        var spec = header[(equals + 1)..].Trim();
        var dash = spec.IndexOf('-', StringComparison.Ordinal);

        if (dash < 0 || spec.Contains(',', StringComparison.Ordinal))
        {
            return whole;
        }

        var first = spec[..dash];
        var last = spec[(dash + 1)..];

        if (first.Length == 0)
        {
            if (!Number(last, out var suffix))
            {
                return whole;
            }

            if (suffix == 0 || size == 0)
            {
                return unsatisfiable;
            }

            var length = Math.Min(suffix, size);
            return new Outcome(Kind.Partial, size - length, length);
        }

        if (!Number(first, out var offset))
        {
            return whole;
        }

        long end = size - 1;

        if (last.Length > 0)
        {
            if (!Number(last, out var named) || named < offset)
            {
                return whole;
            }

            end = Math.Min(named, size - 1);
        }

        return offset >= size ? unsatisfiable : new Outcome(Kind.Partial, offset, end - offset + 1);
    }

    private static bool Number(string text, out long value) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
