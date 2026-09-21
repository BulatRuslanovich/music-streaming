// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Admin;

/// <summary>
/// Разворот по одному слушателю: топы, активность по дням и часам, откуда он включает музыку и
/// что принёс в библиотеку.
/// </summary>
/// <remarks>
/// Повторяет форму <see cref="StatisticsService"/>, но для произвольного пользователя, а не для
/// текущего. Сами запросы общие и живут в <see cref="ListeningAggregates"/>; общим сервисом эти два
/// пути не делаются намеренно: <see cref="StatisticsService"/> замкнут на <see cref="ICurrentUser"/>,
/// а сюда пользователь приходит параметром — и попасть сюда можно только через админскую политику.
/// Здесь же остаётся то, чего у слушателя нет: откуда он включает музыку и что принёс в библиотеку.
/// </remarks>
public class AdminListenerBreakdown(IApplicationDbContext db)
{
    private const int TopSize = 10;
    private const int RecentUploads = 10;

    public async Task<AdminListenerDetailDto> BuildAsync(
        AdminListenerDto listener, AdminPeriodWindow window, CancellationToken ct)
    {
        var scope = ListeningAggregates.ScopeFor(db, listener.Id, window.From);

        return new AdminListenerDetailDto(
            window.Period,
            window.From,
            window.TimeZone,
            listener,
            await ListeningAggregates.TopTracksAsync(db, listener.Id, scope, TopSize, ct),
            await ListeningAggregates.TopArtistsAsync(db, scope, TopSize, ct),
            await ListeningAggregates.TopAlbumsAsync(db, scope, TopSize, ct),
            await ListeningAggregates.TopGenresAsync(db, scope, TopSize, ct),
            await ListeningAggregates.ByDayAsync(db, listener.Id, window.From, window.TimeZone, ct),
            await ListeningAggregates.ByHourAsync(db, listener.Id, window.From, window.TimeZone, ct),
            await BySourceAsync(listener.Id, window.From, ct),
            await RecentUploadsAsync(listener.Id, ct));
    }

    private async Task<IReadOnlyList<PlaybackSourceCountDto>> BySourceAsync(
        Guid userId, DateTimeOffset? from, CancellationToken ct)
    {
        var query = db.PlaybackEvents.AsNoTracking()
            .Where(e => e.UserId == userId && e.Type == PlaybackEventType.TrackStarted);

        if (from is { } start)
            query = query.Where(e => e.OccurredAt >= start);

        // Проекция в record поверх GroupBy обрывает трансляцию, как только на неё вешается
        // сортировка: EF не разбирает такое дерево. Группировка отдаёт анонимный тип, а DTO
        // собирается уже в памяти — строк здесь по числу мест в приложении, не больше.
        var counts = await query
            .GroupBy(e => e.Source)
            .Select(g => new { Source = g.Key, Plays = g.Count() })
            .OrderByDescending(x => x.Plays)
            .ToListAsync(ct);

        return [.. counts.Select(c => new PlaybackSourceCountDto(c.Source, c.Plays))];
    }

    /// <summary>Последнее принесённое — без ограничения периодом: список всегда должен что-то показать.</summary>
    private Task<List<AdminUploadedTrackDto>> RecentUploadsAsync(Guid userId, CancellationToken ct) =>
        db.Tracks.AsNoTracking()
            .Where(t => t.AddedByUserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Take(RecentUploads)
            .Select(t => new AdminUploadedTrackDto(
                t.Id, t.Title, t.Artist!.Name, t.CreatedAt, t.FileSize))
            .ToListAsync(ct);
}
