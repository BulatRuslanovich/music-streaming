// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using TagLib;

namespace Infrastructure.Metadata;

// Записывает теги в файл до загрузки в библиотеку — дальше его читает TagLibAudioMetadataReader,
// как любой загруженный вручную файл.
public class TagLibAudioMetadataWriter
{
    public void Write(string absolutePath, string tagLibMimeType, AudioTags tags)
    {
        using var file = TagLib.File.Create(absolutePath, tagLibMimeType, ReadStyle.Average);

        // Без явного создания у «голого» файла нет тега, и запись в file.Tag ничего не сохранит.
        file.GetTag(file is TagLib.Flac.File ? TagTypes.Xiph : TagTypes.Id3v2, create: true);

        var tag = file.Tag;
        tag.Title = tags.Title;
        tag.Performers = [.. tags.Artists];
        tag.AlbumArtists = [.. tags.AlbumArtists];
        tag.Album = tags.Album;
        tag.Genres = tags.Genre is { } genre ? [genre] : [];
        tag.Year = (uint)(tags.Year ?? 0);
        tag.Track = (uint)(tags.TrackNumber ?? 0);
        tag.Disc = (uint)(tags.DiscNumber ?? 0);

        if (tags.Cover is { Length: > 0 } cover)
        {
            tag.Pictures =
            [
                new Picture(new ByteVector(cover))
                {
                    Type = PictureType.FrontCover,
                    MimeType = cover is [0x89, 0x50, 0x4E, 0x47, ..] ? "image/png" : "image/jpeg",
                },
            ];
        }

        file.Save();
    }
}

public record AudioTags(
    string Title,
    IReadOnlyList<string> Artists,
    IReadOnlyList<string> AlbumArtists,
    string? Album,
    string? Genre,
    int? Year,
    int? TrackNumber,
    int? DiscNumber,
    byte[]? Cover);
