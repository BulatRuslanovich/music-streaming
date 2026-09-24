// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

/// <summary>When the background passes run and how long their inputs are kept.</summary>
public class RecommendationMaintenanceOptions
{
    public int RegenerationDebounceSeconds { get; set; } = 60;

    /// <summary>Потолок задержки пересборки: непрерывная активность не должна откладывать её вечно.</summary>
    public int RegenerationMaxDelaySeconds { get; set; } = 300;

    public int SimilarityIntervalHours { get; set; } = 6;
    public int StartupDelaySeconds { get; set; } = 30;
    public int EventRetentionDays { get; set; } = 180;
    public int ImpressionRetentionDays { get; set; } = 60;

    /// <summary>How long the per-hour listening rollup is kept.</summary>
    /// <remarks>
    /// Всё, что её читает — итоги месяца, статистика слушателя, вкус по частям суток, — берёт
    /// окно, а не всю историю. Два года покрывают любое такое окно с запасом, а держалась она
    /// вечно: порядка семидесяти строк на слушателя в день, и ни одного удаления.
    /// </remarks>
    public int ListeningStatRetentionDays { get; set; } = 730;
    public int MaxEventsPerRequest { get; set; } = 100;

    internal static OptionsBuilder<RecommendationOptions> Validate(
        OptionsBuilder<RecommendationOptions> builder) => builder
        .Validate(o => o.Maintenance.RegenerationDebounceSeconds > 0, "Recommendations:Maintenance:RegenerationDebounceSeconds must be greater than zero.")
        .Validate(
            o => o.Maintenance.RegenerationMaxDelaySeconds >= o.Maintenance.RegenerationDebounceSeconds,
            "Recommendations:Maintenance:RegenerationMaxDelaySeconds must be at least Recommendations:Maintenance:RegenerationDebounceSeconds.")
        .Validate(o => o.Maintenance.SimilarityIntervalHours > 0, "Recommendations:Maintenance:SimilarityIntervalHours must be greater than zero.")
        .Validate(o => o.Maintenance.StartupDelaySeconds >= 0, "Recommendations:Maintenance:StartupDelaySeconds must not be negative.")
        .Validate(o => o.Maintenance.EventRetentionDays > 0, "Recommendations:Maintenance:EventRetentionDays must be greater than zero.")
        // Впечатления живут парой с событиями и чистятся тем же проходом — правило у них общее.
        .Validate(o => o.Maintenance.ImpressionRetentionDays > 0, "Recommendations:Maintenance:ImpressionRetentionDays must be greater than zero.")
        .Validate(o => o.Maintenance.ListeningStatRetentionDays > 0, "Recommendations:Maintenance:ListeningStatRetentionDays must be greater than zero.")
        .Validate(o => o.Maintenance.MaxEventsPerRequest > 0, "Recommendations:Maintenance:MaxEventsPerRequest must be greater than zero.");
}
