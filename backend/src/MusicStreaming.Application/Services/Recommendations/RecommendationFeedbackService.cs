// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>
/// Явный фидбек по рекомендациям: «не интересно» и его отмена. Отделено от отдачи полок — это
/// единственная запись-ориентированная часть подсистемы, и у неё свои эндпоинты.
/// </summary>
public class RecommendationFeedbackService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    RecommendationRefreshQueue refreshQueue,
    IMemoryCache memoryCache,
    IOptions<RecommendationOptions> options,
    TimeProvider clock)
{
    private RecommendationOptions Options => options.Value;

    /// <summary>
    /// Явное «не интересно». Неявный дизлайк выводится из пропусков и всегда спорен — здесь человек
    /// говорит прямо, поэтому подавление жёсткое: кандидат просто не попадает в пул.
    /// </summary>
    public async Task<RecommendationSuppressionDto> SuppressAsync(
        RecommendationFeedbackRequest request, CancellationToken ct = default)
    {
        await EnsureTargetExistsAsync(request, ct);

        var userId = currentUser.Id;
        var now = clock.GetUtcNow();

        var existing = await db.RecommendationSuppressions
            .FirstOrDefaultAsync(
                s => s.UserId == userId && s.Target == request.Target && s.TargetId == request.TargetId,
                ct);

        // Артист блокируется навсегда: это решение о вкусе, а не о конкретной записи.
        var expiresAt = request.Target == SuppressionTarget.Artist || Options.TrackSuppressionDays <= 0
            ? (DateTimeOffset?)null
            : now.AddDays(Options.TrackSuppressionDays);

        if (existing is null)
        {
            existing = new RecommendationSuppression
            {
                UserId = userId,
                Target = request.Target,
                TargetId = request.TargetId,
                CreatedAt = now,
                ExpiresAt = expiresAt,
            };

            db.RecommendationSuppressions.Add(existing);
        }
        else
        {
            existing.CreatedAt = now;
            existing.ExpiresAt = expiresAt;
        }

        await db.SaveChangesAsync(ct);
        InvalidateShelves(userId, now);

        return new RecommendationSuppressionDto(
            existing.Target, existing.TargetId, existing.CreatedAt, existing.ExpiresAt);
    }

    public async Task RestoreAsync(
        SuppressionTarget target, Guid targetId, CancellationToken ct = default)
    {
        var userId = currentUser.Id;

        var existing = await db.RecommendationSuppressions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Target == target && s.TargetId == targetId, ct)
            ?? throw new NotFoundException("Feedback not found.");

        db.RecommendationSuppressions.Remove(existing);
        await db.SaveChangesAsync(ct);

        InvalidateShelves(userId, clock.GetUtcNow());
    }

    public async Task<IReadOnlyList<RecommendationSuppressionDto>> GetSuppressionsAsync(
        CancellationToken ct = default)
    {
        var userId = currentUser.Id;
        var now = clock.GetUtcNow();

        return await db.RecommendationSuppressions.AsNoTracking()
            .Where(s => s.UserId == userId && (s.ExpiresAt == null || s.ExpiresAt > now))
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new RecommendationSuppressionDto(s.Target, s.TargetId, s.CreatedAt, s.ExpiresAt))
            .ToListAsync(ct);
    }

    private async Task EnsureTargetExistsAsync(RecommendationFeedbackRequest request, CancellationToken ct)
    {
        var exists = request.Target switch
        {
            SuppressionTarget.Track => await db.Tracks.AnyAsync(t => t.Id == request.TargetId, ct),
            SuppressionTarget.Artist => await db.Artists.AnyAsync(a => a.Id == request.TargetId, ct),
            _ => throw new ValidationException("Unknown feedback target."),
        };

        if (!exists)
            throw new NotFoundException("Feedback target not found.");
    }

    /// <summary>Полки, собранные до фидбека, всё ещё содержат подавленное — пересобрать их сразу.</summary>
    private void InvalidateShelves(Guid userId, DateTimeOffset now)
    {
        memoryCache.Remove(RecommendationCacheKeys.Shelves(userId));
        refreshQueue.MarkDirty(userId, now, forceRebuild: true);
    }
}
