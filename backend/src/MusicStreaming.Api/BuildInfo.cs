using System.Reflection;
using MusicStreaming.Api.Controllers;

namespace MusicStreaming.Api;

public static class BuildInfo
{
    private const int ShortShaLength = 7;

    public static SystemInfoDto Current { get; } = Resolve();

    private static SystemInfoDto Resolve()
    {
        var assembly = typeof(BuildInfo).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        var version = assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        string? commit = null;

        if (!string.IsNullOrEmpty(informational))
        {
            var plus = informational.IndexOf('+');

            if (plus < 0)
            {
                version = informational;
            }
            else
            {
                version = informational[..plus];
                var sha = informational[(plus + 1)..];

                if (sha.Length > 0)
                    commit = sha.Length > ShortShaLength ? sha[..ShortShaLength] : sha;
            }
        }

        return new SystemInfoDto(version, commit, ReadBuiltAt(assembly));
    }

    private static DateTimeOffset? ReadBuiltAt(Assembly assembly)
    {
        try
        {
            var location = assembly.Location;

            if (string.IsNullOrEmpty(location))
            {
                return null;
            } 
            else
            {
                return new DateTimeOffset(File.GetLastWriteTimeUtc(location), TimeSpan.Zero);
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
