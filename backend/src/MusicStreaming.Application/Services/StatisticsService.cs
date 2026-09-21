// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services;

public class StatisticsService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    UserSettingsService settings)
{
    private const int TopSize = 10;

    public async Task<StatisticsDto> GetAsync(
        StatisticsPeriod period, CancellationToken ct)
    {
        var timeZone = (await settings.GetAsync(ct)).TimeZone;
        var from = await ResolveStartAsync(period, timeZone, ct);

        var scope = ScopeFrom(from);

        var byDay = await ListeningAggregates.ByDayAsync(db, currentUser.Id, from, timeZone, ct);
        var byHour = await ListeningAggregates.ByHourAsync(db, currentUser.Id, from, timeZone, ct);

        return new StatisticsDto(
            period,
            from,
            timeZone,
            await SummariseAsync(scope, byDay, byHour, ct),
            await ListeningAggregates.TopTracksAsync(db, currentUser.Id, scope, TopSize, ct),
            await ListeningAggregates.TopArtistsAsync(db, scope, TopSize, ct),
            await ListeningAggregates.TopAlbumsAsync(db, scope, TopSize, ct),
            await ListeningAggregates.TopGenresAsync(db, scope, TopSize, ct),
            byDay,
            byHour);
    }

    public async Task<IReadOnlyList<StatisticsTrackDto>> TopTracksAsync(
        StatisticsPeriod period, int size, CancellationToken ct)
    {
        var timeZone = (await settings.GetAsync(ct)).TimeZone;
        var from = await ResolveStartAsync(period, timeZone, ct);

        return await ListeningAggregates.TopTracksAsync(db, currentUser.Id, ScopeFrom(from), size, ct);
    }

    /// <summary>
    /// Область всегда замкнута на текущего слушателя: этот сервис намеренно не умеет показывать
    /// чужую статистику. Чужую показывает админский путь, у которого свои права.
    /// </summary>
    private IQueryable<ListeningStat> ScopeFrom(DateTimeOffset? from) =>
        ListeningAggregates.ScopeFor(db, currentUser.Id, from);

    private Task<DateTimeOffset?> ResolveStartAsync(
        StatisticsPeriod period, string timeZone, CancellationToken ct) =>
        StatisticsPeriods.StartAsync(db, period, timeZone, ct);

    private async Task<StatisticsSummaryDto> SummariseAsync(
        IQueryable<ListeningStat> scope,
        IReadOnlyList<DailyActivityDto> byDay,
        IReadOnlyList<HourlyActivityDto> byHour,
        CancellationToken ct)
    {
        var totals = await scope
            .GroupBy(_ => 1)
            .Select(group => new
            {
                ListenedSeconds = group.Sum(s => s.ListenedSeconds),
                Plays = group.Sum(s => s.PlayCount),
                UniqueTracks = group.Select(s => s.TrackId).Distinct().Count(),
                UniqueAlbums = group
                    .Where(s => s.Track!.AlbumId != null)
                    .Select(s => s.Track!.AlbumId)
                    .Distinct()
                    .Count(),
            })
            .SingleOrDefaultAsync(ct);

        var uniqueArtists = await db.TrackArtists
            .Where(credit => scope.Select(stat => stat.TrackId).Contains(credit.TrackId))
            .Select(credit => credit.ArtistId)
            .Distinct()
            .CountAsync(ct);

        return new StatisticsSummaryDto(
            totals?.ListenedSeconds ?? 0,
            totals?.Plays ?? 0,
            totals?.UniqueTracks ?? 0,
            uniqueArtists,
            totals?.UniqueAlbums ?? 0,
            byDay.Count,
            byDay.MaxBy(day => day.ListenedSeconds));
    }
}
