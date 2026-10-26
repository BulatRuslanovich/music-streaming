// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Common;
using App.Dtos;
using Infrastructure.Integrations;

namespace App.Services.Integrations;

public class YandexMusicService(YandexMusicClient yandex, UploadProbeService probe, YandexImportQueue queue)
{
    private const int TokenMaxLength = 512;

    public async Task<YandexSearchDto> SearchAsync(string? token, string? text, CancellationToken ct)
    {
        var key = RequireToken(token);
        if (Text.TrimToNull(text) is not { } query)
            return new YandexSearchDto([], []);

        var found = await yandex.SearchAsync(key, query, ct);

        // «Уже в библиотеке» — тем же сравнением по тегам, что и при загрузке файлов.
        var verdicts = found.Tracks.Count == 0
            ? []
            : (await probe.ProbeAsync([.. found.Tracks.Select(YandexTrackImporter.ProbeFile)], ct)).Files;

        return new YandexSearchDto(
            [.. found.Tracks.Select((track, index) => new YandexTrackDto(
                track.Id,
                track.Title,
                YandexTrackImporter.Artist(track),
                track.Album?.Title,
                YandexMusicClient.ThumbnailUrl(track.Album?.CoverUri ?? track.CoverUri),
                track.DurationSeconds,
                track.Available,
                verdicts[index].Verdict == UploadProbeVerdict.Duplicate))],
            [.. found.Albums.Select(album => new YandexAlbumDto(
                album.Id,
                album.Title,
                album.Artists.Count > 0 ? string.Join(", ", album.Artists) : "Unknown Artist",
                album.Year,
                album.TrackCount,
                YandexMusicClient.ThumbnailUrl(album.CoverUri)))]);
    }

    public YandexImportDto Start(string? token, StartYandexImportRequest request)
    {
        var key = RequireToken(token);

        if (!Enum.IsDefined(request.Kind))
            throw new ValidationException("Unknown import source.");

        var id = Text.TrimToNull(request.Id);
        if (request.Kind != YandexImportKind.Likes && (id is null || !id.All(char.IsAsciiDigit)))
            throw new ValidationException("Unknown import source.");

        return queue.Enqueue(request.Kind, request.Kind == YandexImportKind.Likes ? null : id, key).ToDto();
    }

    public IReadOnlyList<YandexImportDto> List() => queue.List();

    public void Remove(Guid id)
    {
        if (!queue.Remove(id))
            throw new NotFoundException("Import not found.");
    }

    // Токен хранится в браузере админа и приходит заголовком; сервер его не сохраняет.
    private static string RequireToken(string? token) =>
        Text.TrimToNull(token) switch
        {
            null => throw new ValidationException("Add a Yandex Music token in the settings first."),
            { Length: > TokenMaxLength } => throw new ValidationException("The Yandex Music token is too long."),
            var key => key,
        };
}
