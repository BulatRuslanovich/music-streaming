using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;

namespace MusicStreaming.Infrastructure.Metadata;

/// <summary>
/// Reads ID3v1/ID3v2 tags and stream properties with TagLib#.
///
/// Parsing is also how upload validates file integrity: TagLib# has to find a real MPEG audio
/// header to report a duration, so a renamed non-MP3 fails here rather than at playback time.
/// </summary>
public sealed class TagLibAudioMetadataReader(ILogger<TagLibAudioMetadataReader> logger) : IAudioMetadataReader
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
                CoverMimeType: cover?.MimeType);
        }
        catch (TagLib.CorruptFileException ex)
        {
            logger.LogWarning("Corrupt or non-MP3 file rejected: {Path} ({Message})", absolutePath, ex.Message);
            return null;
        }
        catch (TagLib.UnsupportedFormatException ex)
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

    /// <summary>Prefers a declared front cover, falling back to the first embedded image.</summary>
    private static TagLib.IPicture? FirstUsablePicture(TagLib.Tag tag)
    {
        var pictures = tag.Pictures;
        if (pictures.Length == 0)
            return null;

        var front = pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover);
        var picture = front ?? pictures[0];

        return picture.Data.Count > 0 ? picture : null;
    }

    /// <summary>
    /// Keeps every value of a multi-valued frame rather than only the first: ID3v2.4 stores
    /// several performers as separate values, and taking <c>FirstPerformer</c> dropped the rest.
    /// </summary>
    private static IReadOnlyList<string> CleanNames(string[]? values)
    {
        if (values is null || values.Length == 0)
            return [];

        return values.Select(Clean).OfType<string>().ToList();
    }

    /// <summary>
    /// Trims the value and strips the NUL padding that some taggers leave in fixed-width
    /// ID3v1 fields, which would otherwise end up in the database verbatim.
    /// </summary>
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = value.Replace("\0", string.Empty).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }
}
