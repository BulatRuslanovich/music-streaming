// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class MonthlyRecapService(
    IApplicationDbContext db, ICurrentUser currentUser, UserSettingsService settings, TimeProvider clock)
{
    public async Task<MonthlyRecapDto> GetAsync(string? month, CancellationToken ct)
    {
        var zone = (await settings.GetAsync(ct)).TimeZone;
        var range = RecapMonth.Resolve(month, zone, clock.GetUtcNow());
        if (string.CompareOrdinal(range.Month, "2000-01") < 0)
            throw new ValidationException("Choose a month from January 2000 onwards.");
        var previousMonth = DateTime.ParseExact(range.Month, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)
            .AddMonths(-1).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        var previous = RecapMonth.Resolve(previousMonth, zone, clock.GetUtcNow());
        var history = db.ListeningStats.AsNoTracking()
            .Where(s => s.UserId == currentUser.Id && s.ListenedSeconds > 0);
        var scope = history.Where(s => s.Hour >= range.From && s.Hour < range.Until);
        var prior = history.Where(s => s.Hour >= previous.From && s.Hour < previous.Until);
        var ranked = await scope.GroupBy(s => s.TrackId)
            .Select(g => new { Id = g.Key, Seconds = g.Sum(s => s.ListenedSeconds), Plays = g.Sum(s => s.PlayCount) })
            .OrderByDescending(s => s.Seconds).ThenByDescending(s => s.Plays).ThenBy(s => s.Id)
            .Take(50).ToListAsync(ct);
        var tracks = await db.TracksByIdAsync(currentUser.Id, ranked.Select(s => s.Id), ct);
        var artistTotals = from stat in scope
                           join credit in db.TrackArtists on stat.TrackId equals credit.TrackId
                           group stat by credit.ArtistId into g
                           select new { Id = g.Key, Seconds = g.Sum(s => s.ListenedSeconds), Plays = g.Sum(s => s.PlayCount) };
        // DTO создаётся после фильтра открытий: EF не переводит Contains(new Dto(...).Id).
        var artists = from total in artistTotals
                      join artist in db.Artists on total.Id equals artist.Id
                      orderby total.Seconds descending, total.Plays descending, total.Id
                      select new { total.Id, artist.Name, total.Seconds, total.Plays, HasImage = artist.ImagePath != null };
        var knownArtists = from stat in history.Where(s => s.Hour < range.From)
                           join credit in db.TrackArtists on stat.TrackId equals credit.TrackId
                           select credit.ArtistId;

        return new MonthlyRecapDto(range.Month, zone, range.Until <= clock.GetUtcNow(),
            await scope.SumAsync(s => s.ListenedSeconds, ct),
            await scope.SumAsync(s => s.PlayCount, ct),
            await scope.Select(s => s.TrackId).Distinct().CountAsync(ct),
            await artistTotals.CountAsync(ct),
            await prior.SumAsync(s => s.ListenedSeconds, ct),
            [.. ranked.Where(s => tracks.ContainsKey(s.Id))
                .Select(s => new StatisticsTrackDto(tracks[s.Id], s.Seconds, s.Plays))],
            await artists.Take(5).Select(a => new StatisticsEntryDto(a.Id, a.Name, a.Seconds, a.Plays, a.HasImage)).ToListAsync(ct),
            await artists.Where(a => !knownArtists.Contains(a.Id)).Take(5)
                .Select(a => new StatisticsEntryDto(a.Id, a.Name, a.Seconds, a.Plays, a.HasImage)).ToListAsync(ct),
            await TopGenreAsync(scope, ct), await TopGenreAsync(prior, ct));
    }

    public async Task<Guid> SavePlaylistAsync(SaveRecapPlaylistRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 200)
            throw new ValidationException("Playlist name must contain 1 to 200 characters.");
        var recap = await GetAsync(request.Month, ct);
        if (recap.TopTracks.Count == 0) throw new ValidationException("This month has no tracks.");
        var now = clock.GetUtcNow();
        var playlist = new Playlist
        {
            UserId = currentUser.Id,
            Name = name,
            CreatedAt = now,
            UpdatedAt = now,
            Tracks = [.. recap.TopTracks.Select((entry, index) => new PlaylistTrack
            {
                TrackId = entry.Track.Id, Position = index, AddedAt = now,
            })],
        };
        db.Playlists.Add(playlist);
        await db.SaveChangesAsync(ct);
        return playlist.Id;
    }

    private static Task<string?> TopGenreAsync(IQueryable<ListeningStat> scope, CancellationToken ct) =>
        scope.Where(s => s.Track!.GenreId != null).GroupBy(s => s.Track!.Genre!.Name)
            .OrderByDescending(g => g.Sum(s => s.ListenedSeconds)).ThenBy(g => g.Key)
            .Select(g => g.Key).FirstOrDefaultAsync(ct);
}
