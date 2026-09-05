// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;

namespace MusicStreaming.Application.Services;

public record NormalizationDto(double Gain, bool Available);

public class NormalizationService(IApplicationDbContext db, ILoudnessAnalyzer analyzer)
{
    public async Task<NormalizationDto> GetAsync(Guid id, string mode, CancellationToken ct)
    {
        if (mode is not ("track" or "album")) throw new ValidationException("Unknown normalization mode.");
        var track = await db.Tracks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Track not found.");
        var tracks = mode == "album" && track.AlbumId is { } albumId
            ? await db.Tracks.AsNoTracking().Where(t => t.AlbumId == albumId).ToListAsync(ct)
            : [track];
        var measurements = new List<(LoudnessMeasurement, int)>();
        foreach (var entry in tracks)
        {
            var measurement = await analyzer.GetAsync(entry.FilePath, entry.ContentHash, ct);
            if (measurement is null) return new NormalizationDto(1, false);
            measurements.Add((measurement, entry.DurationSeconds));
        }
        return new NormalizationDto(NormalizationGain.Calculate(measurements), true);
    }
}
