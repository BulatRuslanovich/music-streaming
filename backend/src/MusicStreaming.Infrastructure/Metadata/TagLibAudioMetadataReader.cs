using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Domain.Entities;
using TagLib;

namespace MusicStreaming.Infrastructure.Metadata;

public class TagLibAudioMetadataReader(ILogger<TagLibAudioMetadataReader> logger) : IAudioMetadataReader
{
    public AudioMetadata? Read(string absolutePath, string tagLibMimeType)
    {
        try
        {
            using var file = TagLib.File.Create(absolutePath, tagLibMimeType, TagLib.ReadStyle.Average);

            if (file.Properties is null || file.Properties.MediaTypes == TagLib.MediaTypes.None)
            {
                logger.LogWarning("No audio stream found in {Path}", absolutePath);
                return null;
            }

            var tag = file.Tag;
            var properties = file.Properties;
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
                DurationSeconds: (int)Math.Round(properties.Duration.TotalSeconds),
                CoverData: cover?.Data.Data,
                CoverMimeType: cover?.MimeType,
                Lyrics: Clean(tag.Lyrics),
                SyncedLyrics: ReadSyncedLyrics(file),
                Codec: CodecOf(properties),
                BitrateKbps: properties.AudioBitrate > 0 ? properties.AudioBitrate : null,
                SampleRateHz: properties.AudioSampleRate > 0 ? properties.AudioSampleRate : null,
                BitsPerSample: properties.BitsPerSample > 0 ? properties.BitsPerSample : null);
        }
        catch (CorruptFileException ex)
        {
            logger.LogWarning(
                "Corrupt file, or one that is not what its extension claims, rejected: {Path} ({Message})",
                absolutePath, ex.Message);
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

    private static string? CodecOf(TagLib.Properties properties)
    {
        foreach (var codec in properties.Codecs)
        {
            switch (codec)
            {
                case TagLib.Mpeg4.IsoAudioSampleEntry entry:
                    return entry.BoxType.ToString() == "alac" ? "alac" : "aac";

                case TagLib.Flac.StreamHeader:
                    return "flac";

                case TagLib.Mpeg.AudioHeader:
                    return "mp3";
            }
        }

        return null;
    }

    private static IReadOnlyList<LyricLine> ReadSyncedLyrics(TagLib.File file)
    {
        if (file.GetTag(TagTypes.Id3v2) is not TagLib.Id3v2.Tag id3v2)
            return [];

        var frames = id3v2.GetFrames<TagLib.Id3v2.SynchronisedLyricsFrame>().ToList();
        if (frames.Count == 0)
            return [];

        var frame = frames.FirstOrDefault(f => f.Type == TagLib.Id3v2.SynchedTextType.Lyrics) ?? frames[0];

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
