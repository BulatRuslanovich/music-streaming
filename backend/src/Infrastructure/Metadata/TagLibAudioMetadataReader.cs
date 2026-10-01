// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Logging;
using App.Abstractions;
using Domain.Entities;
using TagLib;

namespace Infrastructure.Metadata;

public class TagLibAudioMetadataReader(ILogger<TagLibAudioMetadataReader> logger) : IAudioMetadataReader
{
    public AudioMetadata? Read(string absolutePath, string tagLibMimeType)
    {
        try
        {
            using var file = TagLib.File.Create(absolutePath, tagLibMimeType, ReadStyle.Average);

            if (file.Properties is null || file.Properties.MediaTypes == MediaTypes.None)
            {
                logger.LogWarning("No audio stream found in {Path}", absolutePath);
                return null;
            }

            var tag = file.Tag;
            var properties = file.Properties;

            var picture = tag.Pictures.FirstOrDefault(p => p.Type == PictureType.FrontCover) ?? tag.Pictures.FirstOrDefault();
            var cover = picture?.Data.Count > 0 ? picture : null;

            var codec = properties.Codecs
                .Select(codec => codec switch
                {
                    TagLib.Mpeg4.IsoAudioSampleEntry entry => entry.BoxType.ToString() == "alac" ? "alac" : "aac",
                    TagLib.Flac.StreamHeader => "flac",
                    TagLib.Mpeg.AudioHeader => "mp3",
                    _ => null,
                })
                .FirstOrDefault(name => name is not null);

            IReadOnlyList<LyricLine> syncedLyrics = [];
            if (file.GetTag(TagTypes.Id3v2) is TagLib.Id3v2.Tag id3V2
                && id3V2.GetFrames<TagLib.Id3v2.SynchronisedLyricsFrame>().ToList() is { Count: > 0 } frames
                && (frames.FirstOrDefault(f => f.Type == TagLib.Id3v2.SynchedTextType.Lyrics) ?? frames[0])
                    is { Format: TagLib.Id3v2.TimestampFormat.AbsoluteMilliseconds, Text.Length: > 0 } frame)
            {
                syncedLyrics = [.. frame.Text
                    .Where(entry => entry.Time >= 0)
                    .Select(entry => new LyricLine((int)entry.Time, Clean(entry.Text) ?? string.Empty))];
            }

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
                SyncedLyrics: syncedLyrics,
                Codec: codec,
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
