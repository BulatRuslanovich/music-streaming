// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics.Metrics;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Application.Services;

public sealed class StreamingMetrics : IDisposable
{
    public const string MeterName = "caimack.streaming";

    private readonly Meter _meter;
    private readonly Counter<long> _hlsPreparing;
    private readonly Counter<long> _hlsTranscodeFailures;
    private readonly Counter<long> _hlsSegmentBytes;
    private readonly Histogram<double> _hlsTranscodeDuration;

    public StreamingMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);
        _hlsPreparing = _meter.CreateCounter<long>(
            "hls_preparing_total", "{request}", "HLS manifests answered with 202 while renditions were prepared.");
        _hlsTranscodeFailures = _meter.CreateCounter<long>(
            "hls_transcode_failures_total", "{failure}", "Failed HLS rendition jobs.");
        _hlsSegmentBytes = _meter.CreateCounter<long>(
            "hls_segment_bytes_total", "By", "HLS segment bytes served by the backend.");
        _hlsTranscodeDuration = _meter.CreateHistogram<double>(
            "hls_transcode_duration_seconds", "s", "Time spent preparing one HLS rendition.");
    }

    public void RecordPreparing() => _hlsPreparing.Add(1);

    public void RecordTranscode(AudioQuality quality, TimeSpan duration, bool succeeded)
    {
        var tag = new KeyValuePair<string, object?>("quality", quality.ToString().ToLowerInvariant());
        _hlsTranscodeDuration.Record(duration.TotalSeconds, tag);
        if (!succeeded)
            _hlsTranscodeFailures.Add(1, tag);
    }

    public void RecordSegment(AudioQuality quality, long bytes) =>
        _hlsSegmentBytes.Add(
            bytes,
            new KeyValuePair<string, object?>("quality", quality.ToString().ToLowerInvariant()));

    public void Dispose() => _meter.Dispose();
}
