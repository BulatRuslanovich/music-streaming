// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Domain.Entities;

namespace MusicStreaming.Application.Common;

/// <summary>
/// Куски запросов, которые повторялись слово в слово в четырёх–пяти сервисах. Смысл выноса не в
/// экономии строк, а в том, что «живой токен» и «трек существует» — по одному решению на всю
/// систему, и расходиться копиям здесь нельзя.
/// </summary>
public static class DbSetExtensions
{
    /// <summary>Бросает 404, если трека нет. Проверка перед записью в четырёх сервисах.</summary>
    public static async Task RequireTrackAsync(
        this IApplicationDbContext db, Guid trackId, CancellationToken ct = default)
    {
        if (!await db.Tracks.AnyAsync(t => t.Id == trackId, ct))
            throw new NotFoundException("Track not found.");
    }

    public static async Task RequireArtistAsync(
        this IApplicationDbContext db, Guid artistId, CancellationToken ct = default)
    {
        if (!await db.Artists.AnyAsync(a => a.Id == artistId, ct))
            throw new NotFoundException("Artist not found.");
    }

    public static async Task RequireGenreAsync(
        this IApplicationDbContext db, Guid genreId, CancellationToken ct = default)
    {
        if (!await db.Genres.AnyAsync(g => g.Id == genreId, ct))
            throw new NotFoundException("Genre not found.");
    }

    /// <summary>Токены обновления, которые ещё действуют: не отозваны и не истекли.</summary>
    public static IQueryable<RefreshToken> Live(
        this IQueryable<RefreshToken> tokens, Guid userId, DateTimeOffset now) =>
        tokens.Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now);

    /// <summary>
    /// Отзывает все неотозванные токены пользователя. Истёкшие не трогаются намеренно: они и так
    /// не пускают, а отметка отзыва на них сделала бы историю сессий менее читаемой.
    /// </summary>
    public static Task<int> RevokeAllAsync(
        this IQueryable<RefreshToken> tokens,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        tokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(t => t.SetProperty(token => token.RevokedAt, now), ct);
}
