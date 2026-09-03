// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Admin;

public record AdminListenerFilter(
    StatisticsPeriod Period,
    string? Query,
    AdminListenerSort Sort,
    SortDirection Direction);

/// <summary>
/// Слушатели глазами администратора: строка на человека с его прослушиваниями, загрузками и
/// поведением, плюс разворот по одному из них.
/// </summary>
/// <remarks>
/// <para>
/// Агрегаты собирает один SQL-запрос, а не пять LINQ-подзапросов: нужно левое соединение с
/// пятью источниками сразу, внутри — <c>COUNT(DISTINCT …)</c> и условные счётчики по типу события,
/// и это ровно тот запрос, на трансляцию которого полагаться не стоит. Зато сортировка и
/// пагинация остаются LINQ поверх <c>FromSql</c>: EF заворачивает сырой текст в подзапрос и
/// дописывает <c>ORDER BY</c>/<c>LIMIT</c>/<c>OFFSET</c> сам. Так <c>ORDER BY</c> не приходится
/// склеивать строкой — а параметром он быть не может, — и переиспользуется <see cref="Paging"/>.
/// </para>
/// <para>
/// Все числа отвечают на вопрос «за выбранный период», кроме <c>last_active_at</c>: он намеренно
/// за всё время, иначе «заходил полгода назад» превращается в пустую ячейку.
/// </para>
/// </remarks>
public class AdminListenerStatisticsService(
    IApplicationDbContext db,
    AdminStatisticsScope scope,
    AdminListenerBreakdown breakdown)
{
    public async Task<PagedResult<AdminListenerDto>> GetAsync(
        AdminListenerFilter filter, PageRequest page, CancellationToken ct)
    {
        var window = await scope.ResolveAsync(filter.Period, ct);
        var pattern = SearchTerm.For(filter.Query)?.Pattern;

        var paged = await Order(Rows(window.From, pattern, null), filter)
            .ToPagedAsync(page, row => row, ct);

        return new PagedResult<AdminListenerDto>(
            [.. paged.Items.Select(Map)], paged.Total, paged.Page, paged.PageSize);
    }

    public async Task<AdminListenerDetailDto> GetDetailAsync(
        Guid userId, StatisticsPeriod period, CancellationToken ct)
    {
        var window = await scope.ResolveAsync(period, ct);

        // Пользователь без единого события всё равно приходит строкой с нулями: соединения левые,
        // и 404 здесь означало бы только «такого аккаунта нет».
        var row = await Rows(window.From, null, userId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("User not found.");

        return await breakdown.BuildAsync(Map(row), window, ct);
    }

    private IQueryable<AdminListenerRow> Rows(DateTimeOffset? from, string? pattern, Guid? userId) =>
        db.Set<AdminListenerRow>().FromSql(
            $"""
            SELECT u.id                                    AS id,
                   u.username                              AS username,
                   u.display_name                          AS display_name,
                   u.is_admin                              AS is_admin,
                   u.is_active                             AS is_active,
                   u.created_at                            AS created_at,
                   ev.last_active_at                       AS last_active_at,
                   COALESCE(ls.listened_seconds, 0)        AS listened_seconds,
                   COALESCE(ls.plays, 0)                   AS plays,
                   COALESCE(ls.unique_tracks, 0)           AS unique_tracks,
                   COALESCE(up.uploaded_tracks, 0)         AS uploaded_tracks,
                   COALESCE(up.uploaded_bytes, 0)          AS uploaded_bytes,
                   COALESCE(fav.likes, 0)                  AS likes,
                   COALESCE(pl.playlists, 0)               AS playlists,
                   COALESCE(ev.completed, 0)               AS completed,
                   COALESCE(ev.skipped, 0)                 AS skipped
            FROM users u
            LEFT JOIN (
                SELECT user_id,
                       SUM(listened_seconds)::bigint  AS listened_seconds,
                       SUM(play_count)::int           AS plays,
                       COUNT(DISTINCT track_id)::int  AS unique_tracks
                FROM listening_stats
                WHERE ({from}::timestamptz IS NULL OR hour >= {from}::timestamptz)
                GROUP BY user_id
            ) ls ON ls.user_id = u.id
            LEFT JOIN (
                SELECT added_by_user_id                     AS user_id,
                       COUNT(*)::int                        AS uploaded_tracks,
                       COALESCE(SUM(file_size), 0)::bigint  AS uploaded_bytes
                FROM tracks
                WHERE added_by_user_id IS NOT NULL
                  AND ({from}::timestamptz IS NULL OR created_at >= {from}::timestamptz)
                GROUP BY added_by_user_id
            ) up ON up.user_id = u.id
            LEFT JOIN (
                SELECT user_id, COUNT(*)::int AS likes
                FROM favorites
                WHERE ({from}::timestamptz IS NULL OR created_at >= {from}::timestamptz)
                GROUP BY user_id
            ) fav ON fav.user_id = u.id
            LEFT JOIN (
                SELECT user_id, COUNT(*)::int AS playlists
                FROM playlists
                WHERE ({from}::timestamptz IS NULL OR created_at >= {from}::timestamptz)
                GROUP BY user_id
            ) pl ON pl.user_id = u.id
            LEFT JOIN (
                SELECT user_id,
                       MAX(occurred_at) AS last_active_at,
                       COUNT(*) FILTER (
                           WHERE type = {(int)PlaybackEventType.TrackCompleted}
                             AND ({from}::timestamptz IS NULL OR occurred_at >= {from}::timestamptz)
                       )::int AS completed,
                       COUNT(*) FILTER (
                           WHERE type = {(int)PlaybackEventType.TrackSkipped}
                             AND ({from}::timestamptz IS NULL OR occurred_at >= {from}::timestamptz)
                       )::int AS skipped
                FROM playback_events
                GROUP BY user_id
            ) ev ON ev.user_id = u.id
            WHERE ({userId}::uuid IS NULL OR u.id = {userId}::uuid)
              AND ({pattern}::text IS NULL
                   OR u.username ILIKE {pattern}
                   OR u.display_name ILIKE {pattern})
            """);

    /// <summary>
    /// Id вторым ключом везде: без него страницы съезжают на людях с одинаковым нулём в колонке
    /// сортировки, а таких большинство.
    /// </summary>
    private static IQueryable<AdminListenerRow> Order(
        IQueryable<AdminListenerRow> rows, AdminListenerFilter filter)
    {
        var descending = filter.Direction == SortDirection.Desc;

        // Skip rate не хранится строкой результата, поэтому сортируется по тому же выражению,
        // что и считается: доля пропусков, ноль при отсутствии событий.
        return filter.Sort switch
        {
            AdminListenerSort.CreatedAt => By(rows, r => r.CreatedAt, descending),
            AdminListenerSort.LastActiveAt => By(rows, r => r.LastActiveAt, descending),
            AdminListenerSort.ListenedSeconds => By(rows, r => r.ListenedSeconds, descending),
            AdminListenerSort.Plays => By(rows, r => r.Plays, descending),
            AdminListenerSort.UploadedTracks => By(rows, r => r.UploadedTracks, descending),
            AdminListenerSort.UploadedBytes => By(rows, r => r.UploadedBytes, descending),
            AdminListenerSort.SkipRate => By(
                rows,
                r => r.Completed + r.Skipped == 0 ? 0 : r.Skipped / (double)(r.Completed + r.Skipped),
                descending),
            _ => By(rows, r => r.Username, descending),
        };
    }

    private static IQueryable<AdminListenerRow> By<TKey>(
        IQueryable<AdminListenerRow> rows,
        Expression<Func<AdminListenerRow, TKey>> key,
        bool descending) =>
        (descending ? rows.OrderByDescending(key) : rows.OrderBy(key)).ThenBy(r => r.Id);

    private static AdminListenerDto Map(AdminListenerRow row) => new(
        row.Id,
        row.Username,
        row.DisplayName,
        row.IsAdmin,
        row.IsActive,
        row.CreatedAt,
        row.LastActiveAt,
        row.ListenedSeconds,
        row.Plays,
        row.UniqueTracks,
        row.UploadedTracks,
        row.UploadedBytes,
        row.Likes,
        row.Playlists,
        SkipRates.Of(row.Completed, row.Skipped));
}

/// <summary>
/// Строка административного списка слушателей. Namespace менять нельзя: полное имя типа записано
/// в снапшот модели.
/// </summary>
public class AdminListenerRow
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastActiveAt { get; set; }
    public long ListenedSeconds { get; set; }
    public int Plays { get; set; }
    public int UniqueTracks { get; set; }
    public int UploadedTracks { get; set; }
    public long UploadedBytes { get; set; }
    public int Likes { get; set; }
    public int Playlists { get; set; }
    public int Completed { get; set; }
    public int Skipped { get; set; }
}
