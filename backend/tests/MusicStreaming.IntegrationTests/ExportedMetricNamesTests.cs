// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using MusicStreaming.Application.Recommendations;
using Xunit;

namespace MusicStreaming.IntegrationTests;


[Collection(nameof(RecommendationApiCollection))]
public partial class ExportedMetricNamesTests(RecommendationApiFixture fixture)
{
    private static readonly string[] Declared =
    [
        "recommendation_requests_total",
        "recommendation_cache_hits_total",
        "recommendation_cache_misses_total",
        "playback_events_ingested_total",
        "playback_events_dropped_total",
        "recommendation_impressions_total",
        "recommendation_clicks_total",
        "recommendation_plays_total",
        "recommendation_skips_total",
        "recommendation_generation_duration_seconds",
        "recommendation_candidates_count",
        "recommendation_completion_rate",
    ];

    private static readonly string[] OwnPrefixes = ["recommendation_", "playback_"];

    [Fact]
    public async Task Every_declared_metric_is_exported_under_its_own_name()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var exported = await ExportedNamesAsync();

        Assert.All(Declared, name => Assert.Contains(name, exported));
    }

    [Fact]
    public async Task Every_metric_a_dashboard_queries_exists()
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var exported = await ExportedNamesAsync();
        var missing = new List<string>();

        foreach (var dashboard in DashboardFiles())
        {
            foreach (var metric in MetricsReferencedBy(dashboard, exported))
            {
                if (!exported.Contains(metric))
                    missing.Add($"{Path.GetFileName(dashboard)}: {metric}");
            }
        }

        Assert.True(
            missing.Count == 0,
            $"Dashboards query metrics that /metrics does not export:{Environment.NewLine}"
            + string.Join(Environment.NewLine, missing));
    }

    private async Task<HashSet<string>> ExportedNamesAsync()
    {
        var metrics = fixture.Services.GetRequiredService<RecommendationMetrics>();

        metrics.RecordRequest("home");
        metrics.RecordCacheHit("memory");
        metrics.RecordCacheMiss("empty");
        metrics.RecordEventsIngested(1);
        metrics.RecordEventsDropped(1, "rejected");
        metrics.RecordImpressions(1, "forYou");
        metrics.RecordClick();
        metrics.RecordPlay();
        metrics.RecordSkip();
        metrics.RecordCompletion(0.5);
        metrics.RecordGeneration(TimeSpan.FromSeconds(1), 100);

        var body = await fixture.CreateAnonymousClient().GetStringAsync("/metrics", Cancel.Token);

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in body.Split('\n'))
        {
            if (!line.StartsWith("# TYPE ", StringComparison.Ordinal))
                continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
                names.Add(parts[2]);
        }

        Assert.NotEmpty(names);
        return names;
    }

    private static IEnumerable<string> DashboardFiles() =>
        Directory.EnumerateFiles(DashboardDirectory, "*.json");

    private static string DashboardDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "deploy", "grafana", "dashboards");
                if (Directory.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("deploy/grafana/dashboards was not found above the test binaries.");
        }
    }

    private static IEnumerable<string> MetricsReferencedBy(string dashboardPath, HashSet<string> exported)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(dashboardPath));

        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var expression in Expressions(document.RootElement))
        {
            if (expression.Contains('{') && expression.Contains("service=", StringComparison.Ordinal))
                continue;

            foreach (Match match in MetricPattern().Matches(expression))
            {
                var name = match.Value;

                if (!OwnPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
                    continue;

                found.Add(exported.Contains(name) ? name : StripHistogramSuffix(name));
            }
        }

        return found;
    }

    private static string StripHistogramSuffix(string name)
    {
        foreach (var suffix in (string[])["_bucket", "_sum", "_count"])
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
                return name[..^suffix.Length];
        }

        return name;
    }

    private static IEnumerable<string> Expressions(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("expr") && property.Value.ValueKind == JsonValueKind.String)
                        yield return property.Value.GetString()!;

                    foreach (var nested in Expressions(property.Value))
                        yield return nested;
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in Expressions(item))
                        yield return nested;
                }

                break;
        }
    }

    [GeneratedRegex(@"\b[a-z][a-z0-9_]*\b")]
    private static partial Regex MetricPattern();
}
