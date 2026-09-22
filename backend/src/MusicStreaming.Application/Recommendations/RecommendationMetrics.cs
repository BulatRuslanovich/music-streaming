// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MusicStreaming.Application.Recommendations;

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
    private readonly Counter<long> _djBatches;
    private readonly Histogram<int> _djTracks;

    public RecommendationMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _requests = _meter.CreateCounter<long>(
            "recommendation_requests_total", "{request}", "Recommendation API requests served.");

        _cacheHits = _meter.CreateCounter<long>(
            "recommendation_cache_hits_total", "{hit}", "Shelf reads served from the precomputed cache.");

        _cacheMisses = _meter.CreateCounter<long>(
            "recommendation_cache_misses_total", "{miss}", "Shelf reads that had to generate on the spot.");

        _eventsIngested = _meter.CreateCounter<long>(
            "playback_events_ingested_total", "{event}", "Behavioural events written to the log.");

        _eventsDropped = _meter.CreateCounter<long>(
            "playback_events_dropped_total", "{event}", "Events dropped as invalid or shed under load.");

        _impressions = _meter.CreateCounter<long>(
            "recommendation_impressions_total", "{impression}", "Recommended tracks shown to the listener.");

        _clicks = _meter.CreateCounter<long>(
            "recommendation_clicks_total", "{click}", "Recommended tracks the listener started.");

        _plays = _meter.CreateCounter<long>(
            "recommendation_plays_total", "{play}", "Plays started from a recommendation shelf.");

        _skips = _meter.CreateCounter<long>(
            "recommendation_skips_total", "{skip}", "Recommended plays abandoned near the start.");

        _generationDuration = _meter.CreateHistogram<double>(
            "recommendation_generation_duration_seconds", "s", "Time spent on one shelf generation pass.");

        _candidateCount = _meter.CreateHistogram<int>(
            "recommendation_candidates_count", "{candidate}", "Candidates considered in one generation pass.");

        _completionRate = _meter.CreateHistogram<double>(
            "recommendation_completion_rate", "{ratio}", "Fraction of a recommended track that was listened to.");

        _djBatches = _meter.CreateCounter<long>(
            "dj_batches_total", "{batch}", "Caimack DJ batches generated.");

        _djTracks = _meter.CreateHistogram<int>(
            "dj_tracks_returned", "{track}", "Tracks returned in a Caimack DJ batch.");
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

    public void RecordPlay(string source = "recommendation") =>
        _plays.Add(1, new KeyValuePair<string, object?>("source", source));

    public void RecordSkip(string source = "recommendation") =>
        _skips.Add(1, new KeyValuePair<string, object?>("source", source));

    public void RecordCompletion(double ratio, string source = "recommendation") =>
        _completionRate.Record(ratio, new KeyValuePair<string, object?>("source", source));

    public void RecordDjBatch(string mode, int tracks)
    {
        var tags = new TagList
        {
            { "mode", mode },
            { "result", tracks == 0 ? "empty" : "success" },
        };

        _djBatches.Add(1, tags);
        _djTracks.Record(tracks, new KeyValuePair<string, object?>("mode", mode));
    }

    public void RecordGeneration(TimeSpan duration, int candidates)
    {
        _generationDuration.Record(duration.TotalSeconds);
        _candidateCount.Record(candidates);
    }

    public void Dispose() => _meter.Dispose();
}
