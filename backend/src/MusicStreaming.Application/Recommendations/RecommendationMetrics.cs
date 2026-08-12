using System.Diagnostics.Metrics;

namespace MusicStreaming.Application.Recommendations;

/// <summary>
/// Инструменты движка рекомендаций, публикуемые на метре <see cref="MeterName"/>.
///
/// <para>
/// Намеренно построено на <c>System.Diagnostics.Metrics</c>, а не на клиенте конкретного
/// поставщика: слой приложения остаётся без зависимости от экспортёра, а куда уходят числа,
/// решает проект API. Prometheus забирает их через экспортёр OpenTelemetry.
/// </para>
/// </summary>
public sealed class RecommendationMetrics : IDisposable
{
    public const string MeterName = "caimack.recommendations";

    private readonly Meter _meter;

    private readonly Counter<long> _requests;
    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _cacheMisses;
    private readonly Counter<long> _eventsIngested;
    private readonly Counter<long> _eventsDropped;
    private readonly Counter<long> _impressions;
    private readonly Counter<long> _clicks;
    private readonly Counter<long> _plays;
    private readonly Counter<long> _skips;
    private readonly Histogram<double> _generationDuration;
    private readonly Histogram<int> _candidateCount;
    private readonly Histogram<double> _completionRate;

    public RecommendationMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _requests = _meter.CreateCounter<long>(
            "recommendation_requests_total", "requests", "Обслуженные запросы к API рекомендаций.");

        _cacheHits = _meter.CreateCounter<long>(
            "recommendation_cache_hits_total", "hits", "Чтения полок, обслуженные из предрассчитанного кэша.");

        _cacheMisses = _meter.CreateCounter<long>(
            "recommendation_cache_misses_total", "misses", "Чтения полок, потребовавшие генерации на месте.");

        _eventsIngested = _meter.CreateCounter<long>(
            "playback_events_ingested_total", "events", "Поведенческие события, записанные в журнал.");

        _eventsDropped = _meter.CreateCounter<long>(
            "playback_events_dropped_total", "events", "Отброшенные события — невалидные или сброшенные под нагрузкой.");

        _impressions = _meter.CreateCounter<long>(
            "recommendation_impressions_total", "impressions", "Рекомендованные треки, показанные пользователю.");

        _clicks = _meter.CreateCounter<long>(
            "recommendation_clicks_total", "clicks", "Рекомендованные треки, которые пользователь включил.");

        _plays = _meter.CreateCounter<long>(
            "recommendation_plays_total", "plays", "Прослушивания, начатые с полки рекомендаций.");

        _skips = _meter.CreateCounter<long>(
            "recommendation_skips_total", "skips", "Рекомендованные прослушивания, брошенные в начале.");

        _generationDuration = _meter.CreateHistogram<double>(
            "recommendation_generation_duration_seconds", "s", "Время одного прохода генерации полок.");

        _candidateCount = _meter.CreateHistogram<int>(
            "recommendation_candidates_count", "candidates", "Кандидаты, рассмотренные за один проход генерации.");

        _completionRate = _meter.CreateHistogram<double>(
            "recommendation_completion_rate", "ratio", "Доля рекомендованного трека, которая была прослушана.");
    }

    public void RecordRequest(string endpoint) =>
        _requests.Add(1, new KeyValuePair<string, object?>("endpoint", endpoint));

    public void RecordCacheHit(string shelf) =>
        _cacheHits.Add(1, new KeyValuePair<string, object?>("shelf", shelf));

    public void RecordCacheMiss(string shelf) =>
        _cacheMisses.Add(1, new KeyValuePair<string, object?>("shelf", shelf));

    public void RecordEventsIngested(int count)
    {
        if (count > 0)
            _eventsIngested.Add(count);
    }

    public void RecordEventsDropped(int count, string reason)
    {
        if (count > 0)
            _eventsDropped.Add(count, new KeyValuePair<string, object?>("reason", reason));
    }

    public void RecordImpressions(int count, string shelf)
    {
        if (count > 0)
            _impressions.Add(count, new KeyValuePair<string, object?>("shelf", shelf));
    }

    public void RecordClick() => _clicks.Add(1);

    public void RecordPlay() => _plays.Add(1);

    public void RecordSkip() => _skips.Add(1);

    public void RecordCompletion(double ratio) => _completionRate.Record(ratio);

    public void RecordGeneration(TimeSpan duration, int candidates)
    {
        _generationDuration.Record(duration.TotalSeconds);
        _candidateCount.Record(candidates);
    }

    public void Dispose() => _meter.Dispose();
}
