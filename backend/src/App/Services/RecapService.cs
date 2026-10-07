// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Infrastructure.Persistence;
using App.Abstractions;
using App.Common;
using App.Dtos;
using Microsoft.EntityFrameworkCore;
using Domain.Entities.Recommendations;
using App.Recommendations.Embeddings;
using App.Recommendations.Moods;

namespace App.Services;

// Итоги месяца собираются из сырых событий на лету: события хранятся полгода
// (EventRetentionDays), за это время итоги и доступны.
public class RecapService(
    ApplicationDbContext db,
    ICurrentUser currentUser,
    UserSettingsService settings,
    EmbeddingIndex index,
    MoodCatalog moods,
    TimeProvider clock)
{
    private const int Top = 5;

    private const int NewArtistPicks = 3;

    private const int Completed = (int)PlaybackEventType.TrackCompleted;

    private const int Skipped = (int)PlaybackEventType.TrackSkipped;

    public async Task<IReadOnlyList<RecapMonthDto>> MonthsAsync(CancellationToken ct)
    {
        var timeZone = (await settings.GetAsync(ct)).TimeZone;
        var userId = currentUser.Id;
        var threshold = HistoryService.ThresholdSeconds;

        return await db.Database.SqlQuery<RecapMonthDto>(
                $"""
                SELECT EXTRACT(YEAR FROM occurred_at AT TIME ZONE {timeZone})::int AS year,
                       EXTRACT(MONTH FROM occurred_at AT TIME ZONE {timeZone})::int AS month,
                       SUM(GREATEST(listened_seconds, 0))::bigint AS listened_seconds
                FROM playback_events
                WHERE user_id = {userId} AND track_id IS NOT NULL AND type IN ({Completed}, {Skipped})
                GROUP BY 1, 2
                HAVING COUNT(*) FILTER (WHERE listened_seconds >= {threshold}) > 0
                ORDER BY 1 DESC, 2 DESC
                """)
            .ToListAsync(ct);
    }

    public async Task<RecapDto> GetAsync(int year, int month, CancellationToken ct)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            throw new ValidationException("Unknown month.");

        var userId = currentUser.Id;
        var zone = TimeZoneInfo.FindSystemTimeZoneById((await settings.GetAsync(ct)).TimeZone);
        var now = clock.GetUtcNow();

        var from = StartOf(year, month, zone);
        var to = StartOf(month == 12 ? year + 1 : year, month % 12 + 1, zone);

        if (from > now)
            throw new NotFoundException("This month has not started yet.");

        var plays = await PlaysAsync(userId, from, to, zone, ct);
        if (!plays.Any(play => play.Seconds >= HistoryService.ThresholdSeconds))
            throw new NotFoundException("Nothing was played this month.");

        var trackIds = plays.Select(play => play.TrackId).Distinct().ToList();
        var facts = await db.Tracks.AsNoTracking()
            .Where(track => trackIds.Contains(track.Id))
            .Select(track => new
            {
                track.Id,
                track.ArtistId,
                track.GenreId,
                Credits = track.TrackArtists.OrderBy(credit => credit.Position).Select(credit => credit.ArtistId).ToList(),
            })
            .ToDictionaryAsync(
                track => track.Id,
                track => new RecapTrackFacts(track.Credits.Count > 0 ? track.Credits : [track.ArtistId], track.GenreId),
                ct);

        var totals = RecapAggregate.Totals(plays, facts, year, month, Top);

        var snapshot = index.Snapshot();
        var moodShares = RecapAggregate.MoodShares(
            plays, snapshot, [.. moods.All.Select(mood => (mood.Key, moods.RanksIn(snapshot, mood)))]);
        var sound = RecapAggregate.SoundOf(plays, snapshot);

        var previousFrom = StartOf(month == 1 ? year - 1 : year, month == 1 ? 12 : month - 1, zone);
        var previous = await db.PlaybackEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.TrackId != null
                        && (e.Type == PlaybackEventType.TrackCompleted || e.Type == PlaybackEventType.TrackSkipped)
                        && e.OccurredAt >= previousFrom && e.OccurredAt < from && e.ListenedSeconds > 0)
            .SumAsync(e => (long)e.ListenedSeconds, ct);

        var (newArtists, newArtistIds) = await DiscoveriesAsync(userId, from, totals.Artists, ct);

        var tracks = await db.TracksByIdAsync(
            userId, totals.Tracks.Select(item => item.Id).Concat(sound is { } soundId ? [soundId] : []), ct);

        var artistIds = totals.Artists.Take(Top).Select(item => item.Id).Concat(newArtistIds).ToList();
        var artists = await db.Artists.AsNoTracking()
            .Where(artist => artistIds.Contains(artist.Id))
            .Select(ToDto.Artist)
            .ToDictionaryAsync(artist => artist.Id, ct);

        var genreIds = totals.Genres.Select(item => item.GenreId).ToList();
        var genres = await db.Genres.AsNoTracking()
            .Where(genre => genreIds.Contains(genre.Id))
            .ToDictionaryAsync(genre => genre.Id, genre => genre.Name, ct);

        return new RecapDto(
            year,
            month,
            Complete: now >= to,
            totals.Seconds,
            totals.Plays,
            totals.DistinctTracks,
            totals.DistinctArtists,
            previous > 0 ? previous : null,
            [.. totals.Tracks
                .Where(item => tracks.ContainsKey(item.Id))
                .Select(item => new RecapTrackDto(tracks[item.Id], item.Plays, item.Seconds))],
            [.. totals.Artists
                .Take(Top)
                .Where(item => artists.ContainsKey(item.Id))
                .Select(item => new RecapArtistDto(artists[item.Id], item.Plays, item.Seconds))],
            [.. totals.Genres
                .Where(item => genres.ContainsKey(item.GenreId))
                .Select(item => new RecapGenreDto(item.GenreId, genres[item.GenreId], item.Share))],
            [.. moodShares.Select(item => new RecapMoodDto(item.Key, item.Share))],
            totals.DaySeconds,
            totals.HourSeconds,
            sound is { } chosen && tracks.TryGetValue(chosen, out var soundTrack) ? soundTrack : null,
            newArtists,
            [.. newArtistIds.Where(artists.ContainsKey).Select(id => artists[id])]);
    }

    private async Task<List<RecapPlay>> PlaysAsync(
        Guid userId, DateTimeOffset from, DateTimeOffset to, TimeZoneInfo zone, CancellationToken ct)
    {
        var events = await db.PlaybackEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.TrackId != null
                        && (e.Type == PlaybackEventType.TrackCompleted || e.Type == PlaybackEventType.TrackSkipped)
                        && e.OccurredAt >= from && e.OccurredAt < to)
            .Select(e => new { TrackId = e.TrackId!.Value, e.OccurredAt, e.ListenedSeconds })
            .ToListAsync(ct);

        return [.. events.Select(e => new RecapPlay(
            e.TrackId, TimeZoneInfo.ConvertTime(e.OccurredAt, zone).DateTime, Math.Max(0, e.ListenedSeconds)))];
    }

    // Артисты, которых до этого месяца ни разу не слушали. Если истории до месяца нет вовсе,
    // «впервые» не отличить от «давно», и открытия не считаются.
    private async Task<(int? Count, IReadOnlyList<Guid> Picks)> DiscoveriesAsync(
        Guid userId, DateTimeOffset from, IReadOnlyList<RecapRanking> monthArtists, CancellationToken ct)
    {
        var earlier = db.PlaybackEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.TrackId != null && e.OccurredAt < from
                        && (e.Type == PlaybackEventType.TrackCompleted || e.Type == PlaybackEventType.TrackSkipped)
                        && e.ListenedSeconds >= HistoryService.ThresholdSeconds);

        if (!await earlier.AnyAsync(ct))
            return (null, []);

        var monthIds = monthArtists.Select(item => item.Id).ToList();
        var known = await db.TrackArtists.AsNoTracking()
            .Where(credit => monthIds.Contains(credit.ArtistId) && earlier.Any(e => e.TrackId == credit.TrackId))
            .Select(credit => credit.ArtistId)
            .Union(db.Tracks
                .Where(track => monthIds.Contains(track.ArtistId) && earlier.Any(e => e.TrackId == track.Id))
                .Select(track => track.ArtistId))
            .ToListAsync(ct);

        var fresh = monthArtists.Where(item => !known.Contains(item.Id)).ToList();

        return (fresh.Count, [.. fresh.Take(NewArtistPicks).Select(item => item.Id)]);
    }

    private static DateTimeOffset StartOf(int year, int month, TimeZoneInfo zone)
    {
        var local = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        // Npgsql пишет в timestamptz только UTC.
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
