using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Domain.Entities;
using TagLib;

namespace MusicStreaming.Infrastructure.Metadata;

public class TagLibAudioMetadataReader(ILogger<TagLibAudioMetadataReader> logger) : IAudioMetadataReader
{
    public AudioMetadata? Read(string absolutePath)
    {
        try
        {
            using var file = TagLib.File.Create(absolutePath, "audio/mpeg", TagLib.ReadStyle.Average);

            if (file.Properties is null || file.Properties.MediaTypes == TagLib.MediaTypes.None)
            {
                logger.LogWarning("No audio stream found in {Path}", absolutePath);
                return null;
            }

            var tag = file.Tag;
            var cover = FirstUsablePicture(tag);

            return new AudioMetadata(
                Title: Clean(tag.Title),
                Artists: CleanNames(tag.Performers),
                AlbumArtists: CleanNames(tag.AlbumArtists),
                Album: Clean(tag.Album),
                Genre: Clean(tag.FirstGenre) ?? CleanNames(tag.Genres).FirstOrDefault(),
                Year: tag.Year is > 0 and < 3000 ? (int)tag.Year : null,
                TrackNumber: tag.Track > 0 ? (int)tag.Track : null,
                DiscNumber: tag.Disc > 0 ? (int)tag.Disc : null,
                DurationSeconds: (int)Math.Round(file.Properties.Duration.TotalSeconds),
                CoverData: cover?.Data.Data,
                CoverMimeType: cover?.MimeType,
                Lyrics: Clean(tag.Lyrics),
                SyncedLyrics: ReadSyncedLyrics(file));
        }
        catch (CorruptFileException ex)
        {
            logger.LogWarning("Corrupt or non-MP3 file rejected: {Path} ({Message})", absolutePath, ex.Message);
            return null;
        }
        catch (UnsupportedFormatException ex)
        {
            logger.LogWarning("Unsupported format rejected: {Path} ({Message})", absolutePath, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read metadata from {Path}", absolutePath);
            return null;
        }
    }

    /// <summary>
    /// Строки из кадра SYLT, если он есть.
    ///
    /// <para>
    /// Обобщённый <see cref="Tag"/> синхронизированный текст не отдаёт вовсе — его приходится брать
    /// прямо из тега ID3v2. Предпочитается кадр с текстом песни (<see cref="TagLib.Id3v2.SynchedTextType.Lyrics"/>):
    /// в том же кадре SYLT хранят и аккорды, и подсказки, а их показывать вместо слов не надо.
    /// </para>
    /// </summary>
    private static IReadOnlyList<LyricLine> ReadSyncedLyrics(TagLib.File file)
    {
        if (file.GetTag(TagTypes.Id3v2) is not TagLib.Id3v2.Tag id3v2)
            return [];

        var frames = id3v2.GetFrames<TagLib.Id3v2.SynchronisedLyricsFrame>().ToList();
        if (frames.Count == 0)
            return [];

        var frame = frames.FirstOrDefault(f => f.Type == TagLib.Id3v2.SynchedTextType.Lyrics) ?? frames[0];

        // Метки в тактах, а не в миллисекундах, без темпа не пересчитать — такой кадр честнее
        // пропустить и оставить простой текст, чем показывать строки не в такт.
        if (frame.Format != TagLib.Id3v2.TimestampFormat.AbsoluteMilliseconds || frame.Text.Length == 0)
            return [];

        return [.. frame.Text
            .Where(entry => entry.Time >= 0)
            .Select(entry => new LyricLine((int)entry.Time, Clean(entry.Text) ?? string.Empty))];
    }

    private static IPicture? FirstUsablePicture(Tag tag)
    {
        var pictures = tag.Pictures;
        if (pictures.Length == 0)
            return null;

        var front = pictures.FirstOrDefault(p => p.Type == PictureType.FrontCover);
        var picture = front ?? pictures[0];

        return picture.Data.Count > 0 ? picture : null;
    }

    private static IReadOnlyList<string> CleanNames(string[]? values)
    {
        if (values is null || values.Length == 0)
            return [];

        return [.. values.Select(Clean).OfType<string>()];
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = value.Replace("\0", string.Empty).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }
}
