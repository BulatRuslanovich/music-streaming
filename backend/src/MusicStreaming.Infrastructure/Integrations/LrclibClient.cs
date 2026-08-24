// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Infrastructure.Integrations;

public class LrclibClient(HttpClient http, IOptions<LrclibOptions> options) : ILyricsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<LyricsLookupResult> LookupAsync(LyricsQuery query, CancellationToken ct)
    {
        foreach (var variant in Variants(query))
        {
            var result = await LookupOnceAsync(variant, ct);
            if (result.Status != LyricsLookupStatus.NotFound)
                return result;
        }

        return LyricsLookupResult.NotFound;
    }

    private static IEnumerable<LyricsQuery> Variants(LyricsQuery query)
    {
        var artist = Translit.ToLatin(query.Artist);
        var title = Translit.ToLatin(query.Title);

        (string Artist, string Title)[] pairs =
        [
            (query.Artist, query.Title),
            (artist, query.Title),
            (artist, title),
        ];

        var seen = new HashSet<(string, string)>();

        foreach (var pair in pairs)
        {
            if (!seen.Add(pair))
                continue;

            yield return query with
            {
                Artist = pair.Artist,
                Title = pair.Title,

                Album = pair.Title == query.Title || query.Album is null
                    ? query.Album
                    : Translit.ToLatin(query.Album),
            };
        }
    }

    private async Task<LyricsLookupResult> LookupOnceAsync(LyricsQuery query, CancellationToken ct)
    {
        if (await GetAsync(query, ct) is { } exact)
            return Describe(exact.ToCandidate());

        var candidates = await SearchAsync(query, ct);
        var best = LyricsMatch.SelectBest(
            candidates.Select(c => c.ToCandidate()), query, options.Value.DurationToleranceSeconds);

        return best is null ? LyricsLookupResult.NotFound : Describe(best);
    }

    private async Task<LrclibRecord?> GetAsync(LyricsQuery query, CancellationToken ct)
    {
        var url = $"{Root}/api/get"
            + $"?artist_name={Uri.EscapeDataString(query.Artist)}"
            + $"&track_name={Uri.EscapeDataString(query.Title)}"
            + $"&duration={query.DurationSeconds}";

        if (!string.IsNullOrWhiteSpace(query.Album))
            url += $"&album_name={Uri.EscapeDataString(query.Album)}";

        using var response = await http.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<LrclibRecord>(JsonOptions, ct);
    }

    private async Task<IReadOnlyList<LrclibRecord>> SearchAsync(LyricsQuery query, CancellationToken ct)
    {
        var url = $"{Root}/api/search"
            + $"?artist_name={Uri.EscapeDataString(query.Artist)}"
            + $"&track_name={Uri.EscapeDataString(query.Title)}";

        return await http.GetFromJsonAsync<List<LrclibRecord>>(url, JsonOptions, ct) ?? [];
    }

    private static LyricsLookupResult Describe(LyricsCandidate candidate)
    {
        if (candidate.Instrumental)
            return LyricsLookupResult.Instrumental;

        if (LyricsMatch.HasText(candidate.Synced))
            return new LyricsLookupResult(LyricsLookupStatus.Found, candidate.Synced, true);

        return LyricsMatch.HasText(candidate.Plain)
            ? new LyricsLookupResult(LyricsLookupStatus.Found, candidate.Plain, false)
            : LyricsLookupResult.NotFound;
    }

    private string Root => options.Value.BaseUrl.TrimEnd('/');

    private sealed record LrclibRecord(
        string? TrackName,
        string? ArtistName,
        double Duration,
        bool Instrumental,
        string? PlainLyrics,
        string? SyncedLyrics)
    {
        public LyricsCandidate ToCandidate() =>
            new(TrackName, ArtistName, Duration, Instrumental, PlainLyrics, SyncedLyrics);
    }
}
