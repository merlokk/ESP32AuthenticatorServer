using System.Reflection;

namespace AuthServer.Host.Startup;

/// <summary>
/// The build's identity, from the assembly's informational version <c>&lt;version&gt;+&lt;commit&gt;</c>:
/// <c>Version</c> in Directory.Build.props or <c>-p:Version</c>, and the commit the SDK takes from
/// git or from <c>-p:SourceRevisionId</c>.
/// </summary>
internal static class ProductVersion
{
    private static readonly (string Version, string? Commit) Parsed = Parse(
        typeof(ProductVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion);

    public static string Value => Parsed.Version;

    /// <summary>The commit the build came from; null when the build had none.</summary>
    public static string? Commit => Parsed.Commit;

    internal static (string Version, string? Commit) Parse(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational))
        {
            return ("0.0.0", null);
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            return (informational, null);
        }

        var commit = informational[(plus + 1)..];
        return (informational[..plus], commit.Length == 0 ? null : commit);
    }
}

internal sealed record VersionResponse(string Version, string? Commit);

/// <param name="Status">Always "ok": an answer at all is the news.</param>
/// <param name="Time">The server's clock, Unix seconds.</param>
internal sealed record PingResponse(string Status, long Time);
