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

    private const int DurationToleranceSeconds = 2;

    public async Task<LyricsLookupResult> LookupAsync(LyricsQuery query, CancellationToken ct)
    {
        var artist = Translit.ToLatin(query.Artist);
        var title = Translit.ToLatin(query.Title);

        LyricsQuery[] variants =
        [
            query,
            query with { Artist = artist },
            query with
            {
                Artist = artist,
                Title = title,
                Album = title == query.Title || query.Album is null ? query.Album : Translit.ToLatin(query.Album),
            },
        ];

        foreach (var variant in variants.DistinctBy(variant => (variant.Artist, variant.Title)))
        {
            var url = $"{Root}/api/get"
                + $"?artist_name={Uri.EscapeDataString(variant.Artist)}"
                + $"&track_name={Uri.EscapeDataString(variant.Title)}"
                + $"&duration={variant.DurationSeconds}";

            if (!string.IsNullOrWhiteSpace(variant.Album))
                url += $"&album_name={Uri.EscapeDataString(variant.Album)}";

            using var response = await http.GetAsync(url, ct);
            LyricsCandidate? candidate = null;

            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                response.EnsureSuccessStatusCode();
                candidate = (await response.Content.ReadFromJsonAsync<LrclibRecord>(JsonOptions, ct))?.ToCandidate();
            }

            if (candidate is null)
            {
                var found = await http.GetFromJsonAsync<List<LrclibRecord>>(
                    $"{Root}/api/search"
                    + $"?artist_name={Uri.EscapeDataString(variant.Artist)}"
                    + $"&track_name={Uri.EscapeDataString(variant.Title)}",
                    JsonOptions,
                    ct) ?? [];

                candidate = LyricsMatch.SelectBest(found.Select(c => c.ToCandidate()), variant, DurationToleranceSeconds);
            }

            var result = candidate switch
            {
                { Instrumental: true } => LyricsLookupResult.Instrumental,
                { } c when LyricsMatch.HasText(c.Synced) => new LyricsLookupResult(LyricsLookupStatus.Found, c.Synced, true),
                { } c when LyricsMatch.HasText(c.Plain) => new LyricsLookupResult(LyricsLookupStatus.Found, c.Plain, false),
                _ => LyricsLookupResult.NotFound,
            };

            if (result.Status != LyricsLookupStatus.NotFound)
                return result;
        }

        return LyricsLookupResult.NotFound;
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
