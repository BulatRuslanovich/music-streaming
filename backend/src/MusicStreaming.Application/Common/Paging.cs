using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace MusicStreaming.Application.Common;

/// <summary>Постраничная выдача — одна на всё приложение.</summary>
public static class Paging
{
    /// <summary>
    /// Считает общее количество и отдаёт запрошенную страницу.
    ///
    /// <para>
    /// Проекция передаётся выражением, а не делегатом, и применяется <em>после</em>
    /// <c>Skip</c>/<c>Take</c>: тогда её переводит в SQL сам провайдер, и из базы приезжают только
    /// нужные колонки нужных строк. Делегат заставил бы вытащить сущности целиком и преобразовать
    /// их в памяти.
    /// </para>
    /// </summary>
    public static async Task<PagedResult<TResult>> ToPagedAsync<TSource, TResult>(
        this IQueryable<TSource> query,
        PageRequest page,
        Expression<Func<TSource, TResult>> projection,
        CancellationToken ct = default)
    {
        var total = await query.CountAsync(ct);

        var items = await query
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(projection)
            .ToListAsync(ct);

        return new PagedResult<TResult>(items, total, page.Page, page.PageSize);
    }
}
