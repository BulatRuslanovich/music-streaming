// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

public class RecommendationService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ShelfGenerationService generation,
    CandidateGenerator generator,
    RecommendationRefreshQueue refreshQueue,
    IMemoryCache memoryCache,
    IOptions<RecommendationOptions> options,
    TimeProvider clock,
    RecommendationMetrics metrics,
    ILogger<RecommendationService> logger)
{
    private static readonly TimeSpan MemoryCacheLifetime = TimeSpan.FromSeconds(60);
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> InlineBuilds = new();
    private RecommendationOptions Options => options.Value;

    public async Task<RecommendationHomeDto> GetHomeAsync(
        int sectionSize, bool includeScores = false, CancellationToken ct = default)
    {
        metrics.RecordRequest("home");

        var userId = currentUser.Id;
        var shelves = await LoadShelvesAsync(userId, ct);

        if (shelves.Count == 0)
            return new RecommendationHomeDto([], IsColdStart: true, GeneratedAt: null);

        var size = Math.Clamp(sectionSize, 1, Options.ShelfSize);
        var sections = await HydrateAsync(userId, shelves, size, includeScores, ct);

        var profile = await db.UserTasteProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        return new RecommendationHomeDto(
            sections,
            profile is null || profile.PositiveSignalCount == 0,
            shelves.Max(s => s.GeneratedAt));
    }

    public async Task<PagedResult<RecommendedTrackDto>> GetTracksAsync(
        PageRequest page, bool includeScores = false, CancellationToken ct = default)
    {
        metrics.RecordRequest("tracks");

        var userId = currentUser.Id;
        var shelves = await LoadShelvesAsync(userId, ct);

        var ranked = shelves
            .SelectMany(shelf => shelf.Payload)
            .Where(item => item.Kind == RecommendedItemKind.Track)
            .GroupBy(item => item.ItemId)
            .Select(group => group.OrderByDescending(item => item.Score).First())
            .OrderByDescending(item => item.Score)
            .ToList();

        var pageItems = ranked.Skip(page.Skip).Take(page.PageSize).ToList();
        var tracks = await db.TracksByIdAsync(userId, pageItems.Select(i => i.ItemId), ct);

        var items = pageItems
            .Where(item => tracks.ContainsKey(item.ItemId))
            .Select(item => ToDto(tracks[item.ItemId], item, includeScores))
            .ToList();

        return new PagedResult<RecommendedTrackDto>(items, ranked.Count, page.Page, page.PageSize);
    }

    public async Task<IReadOnlyList<ArtistDto>> GetArtistsAsync(int limit, CancellationToken ct = default)
    {
        metrics.RecordRequest("artists");
        return await GetEntitiesAsync(
            ShelfKeys.ArtistsForYou, RecommendedItemKind.Artist, limit, db.ArtistsByIdAsync, ct);
    }

    public async Task<IReadOnlyList<AlbumDto>> GetAlbumsAsync(int limit, CancellationToken ct = default)
    {
        metrics.RecordRequest("albums");
        return await GetEntitiesAsync(
            ShelfKeys.AlbumsForYou, RecommendedItemKind.Album, limit, db.AlbumsByIdAsync, ct);
    }

    public async Task<IReadOnlyList<RecommendedTrackDto>> GetSimilarAsync(
        Guid trackId, int limit, bool includeScores = false, CancellationToken ct = default)
    {
        metrics.RecordRequest("similar");

        var seed = await db.Tracks.AsNoTracking()
            .Where(t => t.Id == trackId)
            .Select(t => new { t.Id, t.Title, t.ArtistId, t.GenreId })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Track not found.");

        var size = Math.Clamp(limit, 1, PageRequest.MaxPageSize);

        var neighbours = await db.TrackSimilarities.AsNoTracking()
            .Where(s => s.TrackId == trackId)
            .OrderByDescending(s => s.Score)
            .Take(size)
            .Select(s => new { s.SimilarTrackId, s.Score })
            .ToListAsync(ct);

        var scores = neighbours.ToDictionary(n => n.SimilarTrackId, n => n.Score);
        var order = neighbours.Select(n => n.SimilarTrackId).ToList();

        if (order.Count == 0)
            order = [.. await generator.SameArtistOrGenreAsync(trackId, size, ct)];

        var tracks = await db.TracksByIdAsync(currentUser.Id, order, ct);
        var reason = new RecommendationReasonDto(ReasonKinds.SimilarTo, seed.Title, seed.Id);

        return order
            .Where(tracks.ContainsKey)
            .Select(id => new RecommendedTrackDto(
                tracks[id], reason, includeScores ? scores.GetValueOrDefault(id) : null))
            .ToList();
    }

    private async Task<List<RecommendationCacheEntry>> LoadShelvesAsync(Guid userId, CancellationToken ct)
    {
        var cacheKey = RecommendationCacheKeys.Shelves(userId);

        if (memoryCache.TryGetValue(cacheKey, out List<RecommendationCacheEntry>? cached) && cached is not null)
        {
            metrics.RecordCacheHit("memory");
            return cached;
        }

        var shelves = await ReadShelvesAsync(userId, ct);

        if (shelves.Count == 0)
        {
            metrics.RecordCacheMiss("empty");
            shelves = await BuildOnceAsync(userId, ct);
        }
        else
        {
            var now = clock.GetUtcNow();
            metrics.RecordCacheHit("database");

            if (shelves.Any(s => s.ExpiresAt <= now))
                refreshQueue.MarkDirty(userId, now);
        }

        memoryCache.Set(cacheKey, shelves, MemoryCacheLifetime);
        return shelves;
    }

    private async Task<List<RecommendationCacheEntry>> ReadShelvesAsync(Guid userId, CancellationToken ct) =>
        await db.RecommendationCache.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Position)
            .ToListAsync(ct);

    private async Task<List<RecommendationCacheEntry>> BuildOnceAsync(Guid userId, CancellationToken ct)
    {
        var gate = InlineBuilds.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct);
        try
        {
            var built = await ReadShelvesAsync(userId, ct);
            if (built.Count > 0)
            {
                metrics.RecordCacheHit("database");
                return built;
            }

            return await GenerateInlineAsync(userId, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<RecommendationCacheEntry>> GenerateInlineAsync(Guid userId, CancellationToken ct)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var run = new RecommendationRun
        {
            UserId = userId,
            Trigger = RecommendationTrigger.OnDemand,
            StartedAt = clock.GetUtcNow(),
            Status = RecommendationRunStatus.Succeeded,
        };

        try
        {
            run.CandidateCount = await generation.GenerateAsync(userId, run.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Inline recommendation generation failed for user {UserId}", userId);
            return [];
        }

        var shelves = await ReadShelvesAsync(userId, ct);

        run.ShelfCount = shelves.Count;
        run.DurationMs = (int)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

        db.RecommendationRuns.Add(run);
        await db.SaveChangesAsync(ct);

        metrics.RecordGeneration(
            System.Diagnostics.Stopwatch.GetElapsedTime(startedAt), run.CandidateCount);

        return shelves;
    }

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

    private async Task<SuppressionSet> LoadSuppressionsAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        var rows = await db.RecommendationSuppressions.AsNoTracking()
            .Where(s => s.UserId == userId && (s.ExpiresAt == null || s.ExpiresAt > now))
            .Select(s => new { s.Target, s.TargetId })
            .ToListAsync(ct);

        return new SuppressionSet(
            rows.Where(r => r.Target == SuppressionTarget.Track).Select(r => r.TargetId).ToHashSet(),
            rows.Where(r => r.Target == SuppressionTarget.Artist).Select(r => r.TargetId).ToHashSet());
    }

    private sealed record SuppressionSet(HashSet<Guid> Tracks, HashSet<Guid> Artists)
    {
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

    private async Task<List<RecommendationSectionDto>> HydrateAsync(
        Guid userId,
        List<RecommendationCacheEntry> shelves,
        int sectionSize,
        bool includeScores,
        CancellationToken ct)
    {
        var wanted = shelves
            .SelectMany(shelf => shelf.Payload.Take(sectionSize).Select(item => (shelf, item)))
            .ToList();

        var tracks = await db.TracksByIdAsync(userId, Ids(wanted, RecommendedItemKind.Track), ct);
        var artists = await db.ArtistsByIdAsync(Ids(wanted, RecommendedItemKind.Artist), ct);
        var albums = await db.AlbumsByIdAsync(Ids(wanted, RecommendedItemKind.Album), ct);

        // Полки живут до шести часов, поэтому подавление применяется ещё и при отдаче: иначе
        // «не интересно» не давало бы видимого эффекта до следующей пересборки.
        var suppressed = await LoadSuppressionsAsync(userId, ct);
        tracks = tracks
            .Where(pair => !suppressed.Hides(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        artists = artists
            .Where(pair => !suppressed.Artists.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        var sections = new List<RecommendationSectionDto>(shelves.Count);

        foreach (var shelf in shelves)
        {
            var items = shelf.Payload.Take(sectionSize).ToList();
            if (items.Count == 0)
                continue;

            var reason = ReasonOf(items[0]);
            var section = items[0].Kind switch
            {
                RecommendedItemKind.Artist => new RecommendationSectionDto(
                    shelf.ShelfKey, ShelfKeys.BaseOf(shelf.ShelfKey), reason, null,
                    Resolve(items, artists), null),

                RecommendedItemKind.Album => new RecommendationSectionDto(
                    shelf.ShelfKey, ShelfKeys.BaseOf(shelf.ShelfKey), reason, null, null,
                    Resolve(items, albums)),

                _ => new RecommendationSectionDto(
                    shelf.ShelfKey, ShelfKeys.BaseOf(shelf.ShelfKey), reason,
                    items.Where(item => tracks.ContainsKey(item.ItemId))
                        .Select(item => ToDto(tracks[item.ItemId], item, includeScores))
                        .ToList(),
                    null, null),
            };

            if (SectionIsEmpty(section))
                continue;

            sections.Add(section);
        }

        await RecordImpressionsAsync(userId, sections, ct);

        return sections;
    }

    /// <summary>
    /// Показ засчитывается при отдаче полок, а не при их сборке: перегенерация кэша ещё не значит,
    /// что человек это видел. Не чаще одного раза в сутки на (пользователь, трек, полка) — иначе
    /// каждое открытие главной душило бы весь пул кандидатов через UnclickedImpressionPenalty.
    /// </summary>
    private async Task RecordImpressionsAsync(
        Guid userId, List<RecommendationSectionDto> sections, CancellationToken ct)
    {
        var shown = sections
            .Where(section => section.Tracks is { Count: > 0 })
            .SelectMany(section => section.Tracks!.Select((track, position) =>
                (Shelf: section.Key, TrackId: track.Track.Id, Position: position)))
            .ToList();

        if (shown.Count == 0)
            return;

        var now = clock.GetUtcNow();
        var since = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var trackIds = shown.Select(item => item.TrackId).Distinct().ToList();

        var alreadyShown = (await db.RecommendationImpressions.AsNoTracking()
                .Where(i => i.UserId == userId && i.ShownAt >= since && trackIds.Contains(i.TrackId))
                .Select(i => new { i.TrackId, i.ShelfKey })
                .ToListAsync(ct))
            .Select(i => (i.ShelfKey, i.TrackId))
            .ToHashSet();

        var fresh = shown
            .Where(item => alreadyShown.Add((item.Shelf, item.TrackId)))
            .ToList();

        if (fresh.Count == 0)
            return;

        foreach (var item in fresh)
        {
            db.RecommendationImpressions.Add(new RecommendationImpression
            {
                UserId = userId,
                TrackId = item.TrackId,
                ShelfKey = item.Shelf,
                Position = item.Position,
                ShownAt = now,
            });
        }

        await db.SaveChangesAsync(ct);

        foreach (var group in fresh.GroupBy(item => ShelfKeys.BaseOf(item.Shelf)))
            metrics.RecordImpressions(group.Count(), group.Key);
    }

    private async Task<IReadOnlyList<T>> GetEntitiesAsync<T>(
        string shelfKey,
        RecommendedItemKind kind,
        int limit,
        Func<IEnumerable<Guid>, CancellationToken, Task<Dictionary<Guid, T>>> loader,
        CancellationToken ct)
    {
        var shelves = await LoadShelvesAsync(currentUser.Id, ct);

        var items = shelves
            .Where(shelf => shelf.ShelfKey == shelfKey)
            .SelectMany(shelf => shelf.Payload)
            .Where(item => item.Kind == kind)
            .Take(Math.Clamp(limit, 1, PageRequest.MaxPageSize))
            .ToList();

        if (items.Count == 0)
            return [];

        var loaded = await loader(items.Select(item => item.ItemId), ct);
        return Resolve(items, loaded);
    }

    private static IEnumerable<Guid> Ids(
        List<(RecommendationCacheEntry Shelf, CachedRecommendation Item)> wanted, RecommendedItemKind kind) =>
        wanted.Where(w => w.Item.Kind == kind).Select(w => w.Item.ItemId).Distinct();

    private static List<T> Resolve<T>(List<CachedRecommendation> items, Dictionary<Guid, T> loaded) =>
        items.Where(item => loaded.ContainsKey(item.ItemId)).Select(item => loaded[item.ItemId]).ToList();

    private static bool SectionIsEmpty(RecommendationSectionDto section) =>
        (section.Tracks?.Count ?? 0) == 0
        && (section.Artists?.Count ?? 0) == 0
        && (section.Albums?.Count ?? 0) == 0;

    private static RecommendedTrackDto ToDto(TrackDto track, CachedRecommendation item, bool includeScores) =>
        new(track, ReasonOf(item), includeScores ? item.Score : null);

    private static RecommendationReasonDto ReasonOf(CachedRecommendation item) =>
        new(item.ReasonKind, item.ReasonSubject, item.ReasonSubjectId);

}
