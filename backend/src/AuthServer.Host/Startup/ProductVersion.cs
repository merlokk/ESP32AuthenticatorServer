using System.Reflection;

namespace AuthServer.Host.Startup;

/// <summary>
/// The build's version: <c>-p:Version=...</c> at publish (the Docker build argument
/// <c>VERSION</c>), without the <c>+commit</c> suffix the SDK appends.
/// </summary>
internal static class ProductVersion
{
    public static string Value { get; } = Read();

    private static string Read()
    {
        var informational = typeof(ProductVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.0.0";

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}

internal sealed record VersionResponse(string Version);
