// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using App.Abstractions;
using App.Common;
using App.Dtos;
using App.Recommendations.Embeddings;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Services;

public class DuplicateService(
    ApplicationDbContext db,
    EmbeddingIndex index,
    TrackEditService editor,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    private static readonly HashSet<string> LosslessCodecs =
        new(["flac", "alac", "wav", "pcm", "aiff", "ape", "wavpack"], StringComparer.OrdinalIgnoreCase);

    public async Task<DuplicateReportDto> GetReportAsync(CancellationToken ct)
    {
        var snapshot = index.Snapshot();

        var durations = await db.Tracks.AsNoTracking()
            .Select(track => new { track.Id, track.DurationSeconds })
            .ToDictionaryAsync(track => track.Id, track => track.DurationSeconds, ct);

        var dismissed = (await db.DuplicateDismissals.AsNoTracking()
                .Select(pair => new { pair.FirstTrackId, pair.SecondTrackId })
                .ToListAsync(ct))
            .Select(pair => (pair.FirstTrackId, pair.SecondTrackId))
            .ToHashSet();

        var groups = DuplicateDetector.Find(snapshot, durations, dismissed);
        var tracks = await db.TracksByIdAsync(currentUser.Id, groups.SelectMany(group => group.TrackIds), ct);

        var compared = Enumerable.Range(0, snapshot.Count)
            .Count(row => durations.ContainsKey(snapshot.MetaAt(row).TrackId));

        return new DuplicateReportDto(
            [
                .. groups
                    .Select(group => new DuplicateGroupDto(
                        BestFirst(group.TrackIds.Where(tracks.ContainsKey).Select(id => tracks[id])),
                        group.Similarity))
                    .Where(group => group.Tracks.Count > 1),
            ],
            compared,
            durations.Count);
    }

    public async Task DismissAsync(IReadOnlyList<Guid> trackIds, CancellationToken ct)
    {
        var ids = trackIds.Distinct().ToList();
        if (ids.Count < 2)
            throw new ValidationException("Select at least two tracks.");

        var now = clock.GetUtcNow();

        foreach (var (first, second) in Pairs(ids))
        {
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO duplicate_dismissals (first_track_id, second_track_id, dismissed_at)
                SELECT {first}, {second}, {now}
                WHERE EXISTS (SELECT 1 FROM tracks WHERE id = {first})
                  AND EXISTS (SELECT 1 FROM tracks WHERE id = {second})
                ON CONFLICT DO NOTHING
                """, ct);
        }
    }

    // Оставить один трек из группы: лайки, места в плейлистах и история прослушиваний остальных
    // переезжают на него, и только потом остальные удаляются вместе с файлами.
    public async Task MergeAsync(Guid keepId, IReadOnlyList<Guid> removeIds, CancellationToken ct)
    {
        var remove = removeIds.Where(id => id != keepId).Distinct().ToArray();
        if (remove.Length == 0)
            throw new ValidationException("Select the tracks to remove.");

        await db.RequireTrackAsync(keepId, ct);

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            // Плейлист, где уже есть оставляемый трек, просто теряет копию; в остальных одна из копий
            // (самая ранняя по позиции) становится оставляемым треком.
            await db.Database.ExecuteSqlAsync(
                $"""
                UPDATE playlist_tracks SET track_id = {keepId}
                WHERE id IN (
                    SELECT DISTINCT ON (playlist_id) id FROM playlist_tracks
                    WHERE track_id = ANY({remove})
                      AND playlist_id NOT IN (SELECT playlist_id FROM playlist_tracks WHERE track_id = {keepId})
                    ORDER BY playlist_id, position)
                """, ct);

            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO favorites (user_id, track_id, created_at)
                SELECT user_id, {keepId}, min(created_at) FROM favorites
                WHERE track_id = ANY({remove})
                GROUP BY user_id
                ON CONFLICT (user_id, track_id) DO NOTHING
                """, ct);

            await db.Database.ExecuteSqlAsync(
                $"UPDATE listening_history SET track_id = {keepId} WHERE track_id = ANY({remove})", ct);

            await transaction.CommitAsync(ct);
        }

        await editor.DeleteTracksAsync(remove, ct);
    }

    private static List<TrackDto> BestFirst(IEnumerable<TrackDto> tracks) =>
    [
        .. tracks
            .OrderByDescending(track => track.Codec is { } codec && LosslessCodecs.Contains(codec))
            .ThenByDescending(track => track.BitsPerSample ?? 0)
            .ThenByDescending(track => track.SampleRateHz ?? 0)
            .ThenByDescending(track => track.BitrateKbps ?? 0)
            .ThenBy(track => track.CreatedAt),
    ];

    private static IEnumerable<(Guid First, Guid Second)> Pairs(IReadOnlyList<Guid> ids)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            for (var j = i + 1; j < ids.Count; j++)
                yield return DuplicateDismissal.Ordered(ids[i], ids[j]);
        }
    }
}
