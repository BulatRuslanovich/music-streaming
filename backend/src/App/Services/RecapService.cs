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

// Итоги прошлого месяца показываются только в первую неделю нового (по часам слушателя), как
// отдельное событие, а не архив. Собираются на лету из сырых событий.
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

    private const int ShownDays = 7;

    // null — показывать нечего: не первая неделя месяца или в прошлом месяце ничего не слушали.
    public async Task<RecapDto?> CurrentAsync(CancellationToken ct)
    {
        var userId = currentUser.Id;
        var zone = TimeZoneInfo.FindSystemTimeZoneById((await settings.GetAsync(ct)).TimeZone);

        var today = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);
        if (today.Day > ShownDays)
            return null;

        var recapped = today.AddMonths(-1);
        var (year, month) = (recapped.Year, recapped.Month);

        var from = StartOf(year, month, zone);
        var to = StartOf(today.Year, today.Month, zone);

        var plays = await PlaysAsync(userId, from, to, zone, ct);
        if (!plays.Any(play => play.Seconds >= HistoryService.ThresholdSeconds))
            return null;

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

        var before = recapped.AddMonths(-1);
        var previousFrom = StartOf(before.Year, before.Month, zone);
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

        var topGenre = totals.Genres.Count == 0
            ? null
            : await db.Genres.AsNoTracking()
                .Where(genre => genre.Id == totals.Genres[0].GenreId)
                .Select(genre => genre.Name)
                .FirstOrDefaultAsync(ct);

        return new RecapDto(
            year,
            month,
            totals.Seconds,
            totals.Plays,
            totals.DistinctTracks,
            totals.DistinctArtists,
            previous > 0 ? previous : null,
            [.. totals.Tracks
                .Where(item => tracks.ContainsKey(item.Id))
                .Select(item => new RecapTrackDto(tracks[item.Id], item.Plays))],
            [.. totals.Artists
                .Take(Top)
                .Where(item => artists.ContainsKey(item.Id))
                .Select(item => new RecapArtistDto(artists[item.Id], item.Plays, item.Seconds))],
            topGenre,
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

        var monthIds = monthArtists.Select(item => item.Id).ToList();
        var known = await db.TrackArtists.AsNoTracking()
            .Where(credit => monthIds.Contains(credit.ArtistId) && earlier.Any(e => e.TrackId == credit.TrackId))
            .Select(credit => credit.ArtistId)
            .Union(db.Tracks
                .Where(track => monthIds.Contains(track.ArtistId) && earlier.Any(e => e.TrackId == track.Id))
                .Select(track => track.ArtistId))
            .ToHashSetAsync(ct);

        // Знакомые артисты уже доказывают, что история до месяца есть; без них проверяем отдельно.
        if (known.Count == 0 && !await earlier.AnyAsync(ct))
            return (null, []);

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
