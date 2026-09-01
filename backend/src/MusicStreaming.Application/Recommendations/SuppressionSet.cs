// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Recommendations;

/// <summary>
/// Что пользователь пометил как «не интересно», разложенное на треки и артистов.
/// </summary>
/// <remarks>
/// Читается дважды за цикл рекомендаций: генератором кандидатов — чтобы подавленное не попало в
/// пул, и гидратором — чтобы не показать то, что успело осесть в уже собранной полке. Запрос и
/// разбиение одни на оба места: разойдись они, подавленный трек показался бы ровно в одном из
/// двух путей, и это была бы очень тихая ошибка.
/// </remarks>
public sealed record SuppressionSet(HashSet<Guid> Tracks, HashSet<Guid> Artists)
{
    public static readonly SuppressionSet Empty = new([], []);

    public static async Task<SuppressionSet> LoadAsync(
        IApplicationDbContext db, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.RecommendationSuppressions.AsNoTracking()
            .Where(s => s.UserId == userId && (s.ExpiresAt == null || s.ExpiresAt > now))
            .Select(s => new { s.Target, s.TargetId })
            .ToListAsync(ct);

        return new SuppressionSet(
            [.. rows.Where(r => r.Target == SuppressionTarget.Track).Select(r => r.TargetId)],
            [.. rows.Where(r => r.Target == SuppressionTarget.Artist).Select(r => r.TargetId)]);
    }

    /// <summary>Скрыт ли трек — сам по себе или из-за любого из своих исполнителей.</summary>
    public bool Hides(TrackDto track)
    {
        if (Tracks.Contains(track.Id) || Artists.Contains(track.ArtistId))
            return true;

        foreach (var artist in track.Artists)
        {
            if (Artists.Contains(artist.Id))
                return true;
        }

        return false;
    }
}
