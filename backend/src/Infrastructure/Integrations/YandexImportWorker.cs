// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Globalization;
using App.Common;
using App.Dtos;
using App.Services.Integrations;

namespace Infrastructure.Integrations;

// Импорты выполняются по одному, треки внутри — по очереди с паузой: так Яндекс не видит
// всплеска запросов, а библиотека не получает гонок между загрузками одного альбома.
public class YandexImportWorker(
    IServiceScopeFactory scopeFactory,
    YandexImportQueue queue,
    ILogger<YandexImportWorker> logger) : BackgroundService
{
    private const int TrackPauseMs = 1000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.ReadAllAsync(stoppingToken))
        {
            CultureInfo.CurrentUICulture = job.Language;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, job.Cancelled);
            var ct = cancellation.Token;

            try
            {
                await RunAsync(job, ct, stoppingToken);
                job.Finish();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                job.Finish();
            }
            catch (AppException ex)
            {
                logger.LogInformation("Yandex Music import {JobId} stopped: {Reason}", job.Id, ex.Message);
                job.Fail(ex.LocalizedMessage);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Yandex Music import {JobId} failed", job.Id);
                job.Fail(ErrorText.Localize("The import failed."));
            }
        }
    }

    // ct — отмена импорта или остановка сервера, stoppingToken — только остановка. Начатый трек
    // доделывается даже после отмены импорта: прерванная на полпути загрузка выглядела бы ошибкой.
    private async Task RunAsync(YandexImportJob job, CancellationToken ct, CancellationToken stoppingToken)
    {
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<YandexTrack> tracks;
        IReadOnlyDictionary<string, string> genres;

        using (var scope = scopeFactory.CreateScope())
        {
            var yandex = scope.ServiceProvider.GetRequiredService<YandexMusicClient>();
            string? title;

            (title, tracks) = job.Kind switch
            {
                YandexImportKind.Likes => (null, await yandex.TracksAsync(job.Token, await yandex.LikedTrackIdsAsync(job.Token, ct), ct)),
                YandexImportKind.Album => await AlbumAsync(yandex, job, ct),
                _ => await TrackAsync(yandex, job, ct),
            };

            if (tracks.Count == 0)
                throw new ValidationException("Nothing to import here.");

            genres = await yandex.GenreTitlesAsync(job.Token, ct);
            job.Start(title, tracks.Count);
        }

        foreach (var (index, track) in tracks.Index())
        {
            ct.ThrowIfCancellationRequested();

            var name = $"{YandexTrackImporter.Artist(track)} — {track.Title}";
            job.Importing(name);

            try
            {
                // Своя область на трек: TrackUploadService и DbContext не должны копить состояние между файлами.
                using var scope = scopeFactory.CreateScope();
                var importer = scope.ServiceProvider.GetRequiredService<YandexTrackImporter>();

                if (await importer.ImportAsync(job.Token, track, genres, stoppingToken) == YandexTrackOutcome.Added)
                    job.Added();
                else
                    job.Skipped();
            }
            catch (AppException ex) when (!IsTokenProblem(ex))
            {
                job.Failed(name, ex.LocalizedMessage);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not AppException)
            {
                logger.LogError(ex, "Importing Yandex Music track {YandexId} failed", track.Id);
                job.Failed(name, ErrorText.Localize("The import failed."));
            }

            if (index < tracks.Count - 1)
                await Task.Delay(TrackPauseMs, ct);
        }
    }

    private static async Task<(string?, IReadOnlyList<YandexTrack>)> AlbumAsync(
        YandexMusicClient yandex, YandexImportJob job, CancellationToken ct)
    {
        var album = await yandex.AlbumAsync(job.Token, job.SourceId!, ct);
        return (album.Album.Title, album.Tracks);
    }

    private static async Task<(string?, IReadOnlyList<YandexTrack>)> TrackAsync(
        YandexMusicClient yandex, YandexImportJob job, CancellationToken ct)
    {
        var tracks = await yandex.TracksAsync(job.Token, [job.SourceId!], ct);
        return (tracks is [var track, ..] ? $"{YandexTrackImporter.Artist(track)} — {track.Title}" : null, tracks);
    }

    // Отозванный токен сломает и все следующие треки — тогда останавливается весь импорт.
    private static bool IsTokenProblem(AppException ex) => ex.Template == "Yandex Music rejected the token.";
}
