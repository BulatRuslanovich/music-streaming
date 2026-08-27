// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Collections.Concurrent;
using System.Reflection;

namespace MusicStreaming.Infrastructure.Recommendations.Sql;

/// <summary>
/// Запросы пересчёта схожести лежат рядом как <c>.sql</c>, а не строками в C#: они длиннее
/// самого класса, и в отдельном файле их видно редактору — с подсветкой и форматированием.
/// </summary>
internal static class SimilaritySql
{
    private static readonly Assembly Assembly = typeof(SimilaritySql).Assembly;
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    public static string RefreshTrackStats => Read("refresh-track-stats");
    public static string AnalyzeInputs => Read("analyze-inputs");
    public static string DirtyTracks => Read("dirty-tracks");
    public static string RewriteState => Read("rewrite-state");
    public static string Scope => Read("scope");
    public static string BuildPairs => Read("build-pairs");
    public static string Score => Read("score");

    private static string Read(string name) => Cache.GetOrAdd(name, static key =>
    {
        var resource = $"{typeof(SimilaritySql).Namespace}.{key}.sql";

        using var stream = Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded SQL resource '{resource}' is missing.");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    });
}
