// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Services;
using MusicStreaming.Infrastructure.Persistence;

namespace MusicStreaming.Infrastructure.Audio;

/// <summary>Measures integrated loudness for the tracks the normalization endpoint asked about.</summary>
/// <remarks>
/// Замер вынесен сюда из обработчика запроса: он декодирует запись целиком, и в альбомном режиме
/// таких замеров столько же, сколько треков в альбоме. Дозаполнения нет — очередь наполняет только
/// то, что действительно слушают.
/// </remarks>
public class LoudnessWorker(
    IServiceScopeFactory scopeFactory,
    LoudnessQueue queue,
    ILoudnessAnalyzer analyzer,
    ILogger<LoudnessWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!analyzer.IsAvailable)
        {
            logger.LogInformation("Loudness measurement is off: ffmpeg is not usable");
            return;
        }

        await queue.ConsumeAsync(
            MeasureAsync,
            (trackId, ex) => logger.LogError(ex, "Loudness measurement of track {TrackId} failed", trackId),
            stoppingToken);
    }

    private async Task MeasureAsync(Guid trackId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var track = await db.Tracks.AsNoTracking()
            .Where(item => item.Id == trackId)
            .Select(item => new { item.FilePath, item.ContentHash })
            .FirstOrDefaultAsync(ct);

        if (track is null)
            return;

        var startedAt = Stopwatch.GetTimestamp();
        var measurement = await analyzer.MeasureAsync(track.FilePath, track.ContentHash, ct);

        if (measurement is null)
        {
            logger.LogDebug("Loudness of track {TrackId} could not be measured", trackId);
            return;
        }

        logger.LogInformation(
            "Measured loudness of track {TrackId} in {Elapsed:0.0} s",
            trackId,
            Stopwatch.GetElapsedTime(startedAt).TotalSeconds);
    }
}
