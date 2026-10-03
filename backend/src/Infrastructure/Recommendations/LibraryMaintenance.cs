// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using App.Abstractions;
using Infrastructure.Persistence;
using App.Recommendations;

namespace Infrastructure.Recommendations;

public class LibraryMaintenance(
    ApplicationDbContext db,
    IMusicStorage storage,
    IImageStorage images,
    TimeProvider clock,
    ILogger<LibraryMaintenance> logger)
{
    private static readonly Lazy<string> RefreshTrackStatsSql = new(() =>
    {
        const string resource = "Infrastructure.Recommendations.Sql.refresh-track-stats.sql";

        using var stream = typeof(LibraryMaintenance).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded SQL resource '{resource}' is missing.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    });

    public async Task RefreshTrackStatsAsync(CancellationToken ct = default)
    {
        var affected = await db.Database.ExecuteSqlRawAsync(RefreshTrackStatsSql.Value, ct);
        logger.LogDebug("Refreshed statistics for {Count} tracks", affected);
    }

    public async Task PruneAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var eventCutoff = now.AddDays(-RecommendationTuning.Maintenance.EventRetentionDays);

        var events = await db.PlaybackEvents.Where(e => e.OccurredAt < eventCutoff).ExecuteDeleteAsync(ct);

        if (events > 0)
            logger.LogInformation("Pruned {Events} playback events", events);

        var coverPaths = await db.Albums
            .Where(a => !a.Tracks.Any() && a.CoverPath != null)
            .Select(a => a.CoverPath!)
            .ToListAsync(ct);

        var albums = await db.Albums.Where(a => !a.Tracks.Any()).ExecuteDeleteAsync(ct);

        var imagePaths = await db.Artists
            .Where(a => !a.Tracks.Any() && !a.Albums.Any() && !a.TrackCredits.Any() && a.ImagePath != null)
            .Select(a => a.ImagePath!)
            .ToListAsync(ct);

        var artists = await db.Artists
            .Where(a => !a.Tracks.Any() && !a.Albums.Any() && !a.TrackCredits.Any())
            .ExecuteDeleteAsync(ct);

        var genres = await db.Genres.Where(g => !g.Tracks.Any()).ExecuteDeleteAsync(ct);

        foreach (var path in coverPaths)
            images.DeleteCover(path);

        foreach (var path in imagePaths)
            storage.Delete(path);

        if (albums + artists + genres > 0)
        {
            logger.LogInformation(
                "Pruned {Albums} orphaned albums, {Artists} artists and {Genres} genres",
                albums, artists, genres);
        }
    }
}
