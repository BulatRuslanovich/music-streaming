// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Diagnostics.Metrics;

namespace MusicStreaming.Application.Common;

/// <summary>Outcome of the scheduled background passes: library maintenance, backfills, index reloads.</summary>
/// <remarks>
/// Отдельный счётчик, а не строчка в логе: проход обслуживания может перестать сходиться на
/// недели, и единственным признаком до сих пор была запись, которую никто не читает. Похожесть,
/// прогрев рендишенов и перечитывание индекса эмбеддингов — всё это работает тихо и в норме, и
/// в отказе, поэтому отказ должен быть виден снаружи.
/// </remarks>
public sealed class MaintenanceMetrics : IDisposable
{
    public const string MeterName = "caimack.maintenance";

    private readonly Meter _meter;
    private readonly Counter<long> _passes;
    private readonly Counter<long> _failures;

    public MaintenanceMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _passes = _meter.CreateCounter<long>(
            "maintenance_passes_total", "{pass}", "Scheduled maintenance passes that finished.");

        _failures = _meter.CreateCounter<long>(
            "maintenance_failures_total", "{failure}", "Scheduled maintenance passes that threw.");
    }

    public void RecordPass(string worker) =>
        _passes.Add(1, new KeyValuePair<string, object?>("worker", worker));

    public void RecordFailure(string worker) =>
        _failures.Add(1, new KeyValuePair<string, object?>("worker", worker));

    public void Dispose() => _meter.Dispose();
}
