using System.Reflection;
using MusicStreaming.Api.Controllers;

namespace MusicStreaming.Api;

/// <summary>
/// Версия и коммит запущенной сборки — чтобы разработчик, глядя на интерфейс, понимал, что именно
/// сейчас крутится, и замечал рассинхрон фронта с бэком.
/// </summary>
public static class BuildInfo
{
    /// <summary>Столько символов хеша хватает, чтобы найти коммит и не занять полстроки в подвале.</summary>
    private const int ShortShaLength = 7;

    public static SystemInfoDto Current { get; } = Resolve();

    private static SystemInfoDto Resolve()
    {
        var assembly = typeof(BuildInfo).Assembly;

        // .NET SDK сам дописывает "+<sha>" к InformationalVersion, читая .git рядом с исходниками.
        // Локально это работает само, а в образе .git недоступен, и хеш приходит из
        // `-p:SourceRevisionId=` (см. Dockerfile) — формат в обоих случаях одинаковый.
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

    /// <summary>
    /// Время сборки берётся из mtime самой библиотеки, а не из атрибута, сгенерированного MSBuild:
    /// такой атрибут менялся бы при каждой сборке и заставлял перекомпилировать проект на ровном
    /// месте. Docker COPY сохраняет время файла, так что в образе значение остаётся честным.
    /// </summary>
    private static DateTimeOffset? ReadBuiltAt(Assembly assembly)
    {
        try
        {
            var location = assembly.Location;

            return string.IsNullOrEmpty(location)
                ? null
                : new DateTimeOffset(File.GetLastWriteTimeUtc(location), TimeSpan.Zero);
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
