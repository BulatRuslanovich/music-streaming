using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Dtos;
using MusicStreaming.Application.Options;
using MusicStreaming.Application.Recommendations;
using MusicStreaming.Application.Recommendations.Scoring;

namespace MusicStreaming.Application.Services.Recommendations;

/// <summary>
/// Продолжение очереди, когда она закончилась.
///
/// <para>
/// Второго движка рекомендаций здесь нет: контекст пользователя, построение кандидатов, оценка,
/// квоты разнообразия и доля исследования — всё то же самое, чем строятся полки. Своего у радио
/// ровно две вещи: затравка (последний реально сыгравший трек) и исключения (то, что уже в
/// очереди, и то, что слушали только что).
/// </para>
///
/// <para>
/// Состояния радио сервер не хранит. Очередь живёт у клиента, он же присылает её в исключениях,
/// поэтому повторный запрос той же очереди просто вернёт следующую пачку, а не то же самое.
/// </para>
/// </summary>
public class RadioService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    CandidateGenerator generator,
    UserSettingsService settings,
    IOptions<RecommendationOptions> options,
    TimeProvider clock,
    RecommendationMetrics metrics,
    ILogger<RadioService> logger)
{
    /// <summary>Сколько треков радио добавляет за раз.</summary>
    public const int BatchSize = 5;

    /// <summary>Трек, сыгравший за это время, в следующую пачку не попадает; всё, что старше, ранкер и так штрафует.</summary>
    private static readonly TimeSpan JustPlayed = TimeSpan.FromHours(24);

    public async Task<RadioBatchDto> NextAsync(RadioRequest request, CancellationToken ct = default)
    {
        if (!(await settings.GetAsync(ct)).Autoplay)
            return RadioBatchDto.Empty;

        var userId = currentUser.Id;
        var now = clock.GetUtcNow();

        var seedTrackId = request.SeedTrackId ?? await LastPlayedAsync(userId, ct);
        if (seedTrackId is not { } seed)
            return RadioBatchDto.Empty;

        metrics.RecordRequest("radio");

        var context = await generator.LoadContextAsync(userId, now, ct);
        var candidates = await generator.AroundAsync(context, seed, ct);

        var weights = options.Value.WeightsFor(context.Profile.Maturity);
        foreach (var candidate in candidates)
            CandidateScorer.Score(candidate, context.Ranking, weights, options.Value);

        var excluded = Excluded(request, seed, context, now);
        var available = candidates.Where(c => !excluded.Contains(c.TrackId)).ToList();

        var wanted = Math.Clamp(request.Limit ?? BatchSize, 1, BatchSize * 4);
        var picks = Explorer.Compose(
            available,
            wanted,
            options.Value.ExplorationRatio,
            options.Value,
            Explorer.SeedFor(userId, $"radio:{seed}", now));

        if (picks.Count == 0)
        {
            logger.LogDebug("Radio found nothing to continue track {SeedTrackId} with", seed);
            return new RadioBatchDto([], seed);
        }

        var tracks = await db.TracksByIdAsync(userId, picks.Select(p => p.TrackId), ct);

        return new RadioBatchDto(
            [.. picks
                .Where(pick => tracks.ContainsKey(pick.TrackId))
                .Select(pick => new RecommendedTrackDto(
                    tracks[pick.TrackId],
                    new RecommendationReasonDto(pick.ReasonKind, pick.ReasonSubject, pick.ReasonSubjectId),
                    null))],
            seed);
    }

    /// <summary>
    /// Что радио не предложит: сама затравка, всё, что уже стоит в очереди, и всё, что слушали за
    /// последние сутки. История берётся из уже загруженного контекста, поэтому исключения не стоят
    /// ни одного лишнего запроса.
    /// </summary>
    private static HashSet<Guid> Excluded(
        RadioRequest request, Guid seed, UserRecommendationContext context, DateTimeOffset now)
    {
        var excluded = new HashSet<Guid>(request.Exclude ?? []) { seed };
        var since = now - JustPlayed;

        foreach (var (trackId, history) in context.Ranking.History)
        {
            if (history.LastPlayedAt >= since)
                excluded.Add(trackId);
        }

        return excluded;
    }

    /// <summary>Последний трек, который пользователю зашёл, — затравка, когда клиент не назвал свою.</summary>
    private async Task<Guid?> LastPlayedAsync(Guid userId, CancellationToken ct) =>
        await db.UserTrackAffinities.AsNoTracking()
            .Where(a => a.UserId == userId && a.Score > 0)
            .OrderByDescending(a => a.LastPlayedAt)
            .Select(a => (Guid?)a.TrackId)
            .FirstOrDefaultAsync(ct);
}
