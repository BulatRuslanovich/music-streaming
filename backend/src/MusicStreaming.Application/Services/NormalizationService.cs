// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;

namespace MusicStreaming.Application.Services;

public record NormalizationDto(double Gain, bool Available);

/// <summary>
/// Во сколько раз пригасить трек, чтобы он звучал вровень с соседями.
/// </summary>
/// <remarks>
/// Отдаёт только то, что уже посчитано. Недостающее уходит в очередь и приезжает к следующему
/// запросу: сам замер — это ffmpeg по всему файлу, и держать на нём поток запроса нельзя,
/// тем более в альбомном режиме, где таких замеров столько же, сколько треков в альбоме.
/// До первого замера возвращается <c>Available: false</c>, и клиент играет без нормализации —
/// ровно так же, как когда ffmpeg нет вовсе.
/// </remarks>
public class NormalizationService(
    IApplicationDbContext db, ILoudnessAnalyzer analyzer, LoudnessQueue queue)
{
    public async Task<NormalizationDto> GetAsync(Guid id, string mode, CancellationToken ct)
    {
        if (mode is not ("track" or "album"))
            throw new ValidationException("Unknown normalization mode.");

        var track = await db.Tracks.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new { item.Id, item.AlbumId, item.ContentHash, item.DurationSeconds })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Track not found.");

        var scope = mode == "album" && track.AlbumId is { } albumId
            ? await db.Tracks.AsNoTracking()
                .Where(item => item.AlbumId == albumId)
                .Select(item => new { item.Id, item.AlbumId, item.ContentHash, item.DurationSeconds })
                .ToListAsync(ct)
            : [track];

        var measurements = new List<(LoudnessMeasurement, int)>(scope.Count);
        var missing = new List<Guid>();

        foreach (var entry in scope)
        {
            if (await analyzer.CachedAsync(entry.ContentHash, ct) is { } measurement)
                measurements.Add((measurement, entry.DurationSeconds));
            else
                missing.Add(entry.Id);
        }

        // Альбомный режим считается по всем трекам сразу: усреднять по части — значит выдать
        // громкость, которая поменяется, как только доедет остальное.
        if (missing.Count > 0)
        {
            foreach (var trackId in missing)
                queue.TryEnqueue(trackId);

            return new NormalizationDto(1, false);
        }

        return new NormalizationDto(NormalizationGain.Calculate(measurements), true);
    }
}
