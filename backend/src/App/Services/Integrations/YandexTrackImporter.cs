// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Common;
using App.Dtos;
using Infrastructure.Audio;
using Infrastructure.Integrations;
using Infrastructure.Metadata;

namespace App.Services.Integrations;

public enum YandexTrackOutcome
{
    Added,
    Skipped,
}

// Один трек из Яндекс Музыки → файл с тегами → обычная загрузка (TrackUploadService). Дальше трек
// живёт как загруженный вручную: анализ, транскоды, рекомендации.
public class YandexTrackImporter(
    YandexMusicClient yandex,
    FlacRemuxer remuxer,
    TagLibAudioMetadataWriter tagWriter,
    UploadProbeService probe,
    TrackUploadService upload,
    ILogger<YandexTrackImporter> logger)
{
    private static readonly string[] LosslessCodecs = ["flac-mp4", "mp3"];
    private static readonly string[] LossyCodecs = ["mp3"];

    public async Task<YandexTrackOutcome> ImportAsync(
        string token, YandexTrack track, IReadOnlyDictionary<string, string> genres, CancellationToken ct)
    {
        if (!track.Available)
            throw new ValidationException("This track is not available on Yandex Music.");

        // Проверка по тегам до скачивания: имя с .flac — чтобы lossy-копия в библиотеке считалась
        // поводом для замены, а lossless — нет.
        var verdict = (await probe.ProbeAsync([ProbeFile(track)], ct)).Files[0].Verdict;
        if (verdict == UploadProbeVerdict.Duplicate)
            return YandexTrackOutcome.Skipped;

        var file = await yandex.FileInfoAsync(token, track.Id, "lossless", LosslessCodecs, ct)
                   ?? await yandex.FileInfoAsync(token, track.Id, "hq", LossyCodecs, ct)
                   ?? throw new ValidationException("Yandex Music did not give a download link for this track.");

        var lossless = file.Codec is "flac-mp4" or "flac";

        // В библиотеке уже есть MP3, а Яндекс тоже отдаёт только MP3 — заменять нечем.
        if (verdict == UploadProbeVerdict.Upgrade && !lossless)
            return YandexTrackOutcome.Skipped;

        if (!lossless && file.Codec != "mp3")
            throw new ValidationException("Yandex Music offered only {0}, which Caimack does not store.", file.Codec);

        var extension = lossless ? ".flac" : ".mp3";
        var format = AudioUpload.For(extension)!.Value;
        var directory = Path.Combine(Path.GetTempPath(), "caimack-yandex", Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var downloaded = Path.Combine(directory, "source");
            var audio = Path.Combine(directory, $"track{extension}");

            await yandex.DownloadAsync(file.Url, downloaded, ct);

            if (file.Codec == "flac-mp4")
            {
                if (!await remuxer.RemuxAsync(downloaded, audio, ct))
                    throw new ValidationException("The FLAC stream could not be unpacked.");
            }
            else
            {
                File.Move(downloaded, audio);
            }

            var album = track.Album;
            tagWriter.Write(audio, format.MetadataMimeType, new AudioTags(
                Title: track.Title,
                Artists: track.Artists,
                AlbumArtists: album?.Artists is { Count: > 0 } albumArtists ? albumArtists : track.Artists,
                Album: album?.Title,
                Genre: album?.Genre is { } code ? genres.GetValueOrDefault(code, code) : null,
                Year: album?.Year,
                TrackNumber: album?.TrackNumber,
                DiscNumber: album?.DiscNumber,
                Cover: await yandex.CoverAsync(album?.CoverUri ?? track.CoverUri, ct)));

            var result = await upload.UploadAsync(
                new UploadCandidate(FileName(track, extension), format.MimeType, new FileInfo(audio).Length,
                    () => File.OpenRead(audio)),
                ct);

            if (result.Failed is [var failure, ..])
                throw new ValidationException(failure.Reason);

            logger.LogInformation("Imported Yandex Music track {YandexId} ({Codec})", track.Id, file.Codec);
            return YandexTrackOutcome.Added;
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not clean up the import folder {Path}", directory);
            }
        }
    }

    public static UploadProbeFileDto ProbeFile(YandexTrack track) =>
        new(FileName(track, ".flac"), null, track.Title, Artist(track));

    public static string Artist(YandexTrack track) =>
        track.Artists.Count > 0 ? string.Join(", ", track.Artists) : "Unknown Artist";

    // Имя файла остаётся в библиотеке как исходное: «Исполнитель - Название.flac».
    private static string FileName(YandexTrack track, string extension)
    {
        var name = $"{Artist(track)} - {track.Title}";
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)) + extension;
    }
}
