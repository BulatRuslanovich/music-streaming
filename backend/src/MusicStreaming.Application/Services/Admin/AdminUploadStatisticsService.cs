// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Services.Admin;

/// <param name="UserId">null — фильтра по пользователю нет; см. также <paramref name="Source"/>.</param>
/// <param name="Source">
/// null — фильтра нет. <see cref="IngestionSource.DirectoryImport"/> вместе с непустым
/// <paramref name="UserId"/> даёт заведомо пустую выдачу: у импорта пользователя не бывает.
/// </param>
public record AdminUploadFilter(
    StatisticsPeriod Period,
    Guid? UserId,
    IngestionSource? Source,
    string? Query,
    AdminUploadSort Sort,
    SortDirection Direction);

/// <summary>Что попало в библиотеку: список успешных загрузок с их происхождением.</summary>
/// <remarks>
/// Неудачные загрузки здесь не появляются и появиться не могут: в базе их нет вовсе — упавший
/// файл уезжает в карантин на диске, а причина живёт только в логе и в ответе на сам запрос.
/// </remarks>
public class AdminUploadStatisticsService(IApplicationDbContext db, AdminStatisticsScope scope)
{
    public async Task<PagedResult<AdminUploadDto>> GetAsync(
        AdminUploadFilter filter, PageRequest page, CancellationToken ct)
    {
        var window = await scope.ResolveAsync(filter.Period, ct);
        var query = db.Tracks.AsNoTracking();

        if (window.From is { } start)
            query = query.Where(t => t.CreatedAt >= start);

        if (filter.UserId is { } userId)
            query = query.Where(t => t.AddedByUserId == userId);

        if (filter.Source is { } source)
            query = query.Where(t => t.IngestionSource == source);

        if (SearchTerm.For(filter.Query) is { Pattern: var pattern })
        {
            query = query.Where(t =>
                EF.Functions.Like(t.NormalizedTitle, pattern, SearchTerm.EscapeChar)
                || EF.Functions.Like(t.Artist!.NormalizedName, pattern, SearchTerm.EscapeChar));
        }

        return await Order(query, filter).ToPagedAsync(page, Projection, ct);
    }

    private static IQueryable<Track> Order(IQueryable<Track> query, AdminUploadFilter filter)
    {
        var descending = filter.Direction == SortDirection.Desc;

        // Id вторым ключом на каждом варианте: без него страницы «съезжают» на треках, у которых
        // совпал размер или секунда добавления, и одна и та же запись попадает на обе страницы.
        return (filter.Sort, descending) switch
        {
            (AdminUploadSort.FileSize, true) => query.OrderByDescending(t => t.FileSize).ThenBy(t => t.Id),
            (AdminUploadSort.FileSize, false) => query.OrderBy(t => t.FileSize).ThenBy(t => t.Id),
            (AdminUploadSort.Plays, true) =>
                query.OrderByDescending(t => t.History.Count).ThenBy(t => t.Id),
            (AdminUploadSort.Plays, false) =>
                query.OrderBy(t => t.History.Count).ThenBy(t => t.Id),
            (_, false) => query.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id),
            _ => query.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id),
        };
    }

    /// <summary>
    /// Прослушивания считаются по истории, а не берутся из <c>TrackStats</c>: ту таблицу наполняет
    /// рекомендательный конвейер, который выключается целиком настройкой <c>Recommendations:Enabled</c>,
    /// и вместе с ним колонка молча обнулилась бы.
    /// </summary>
    private static Expression<Func<Track, AdminUploadDto>> Projection =>
        t => new AdminUploadDto(
            t.Id,
            t.Title,
            t.Artist!.Name,
            t.CreatedAt,
            t.AddedByUserId,
            t.AddedByUser == null ? null : t.AddedByUser.Username,
            t.IngestionSource,
            t.OriginalFileName,
            t.FileSize,
            t.DurationSeconds,
            t.Codec,
            t.BitrateKbps,
            t.History.Count,
            t.History.Select(h => h.UserId).Distinct().Count());
}
