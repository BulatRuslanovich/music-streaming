// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using App.Options;
using Domain.Common;

namespace Infrastructure.Integrations;

public class LrclibClient(HttpClient http, IOptions<LrclibOptions> options)
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
            LrclibRecord? candidate = null;

            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                response.EnsureSuccessStatusCode();
                candidate = await response.Content.ReadFromJsonAsync<LrclibRecord>(JsonOptions, ct);
            }

            if (candidate is null)
            {
                var found = await http.GetFromJsonAsync<List<LrclibRecord>>(
                    $"{Root}/api/search"
                    + $"?artist_name={Uri.EscapeDataString(variant.Artist)}"
                    + $"&track_name={Uri.EscapeDataString(variant.Title)}",
                    JsonOptions,
                    ct) ?? [];

                var wantedTitle = Key(variant.Title);
                var wantedArtist = Key(variant.Artist);

                candidate = found
                    .Where(c => c.TrackName is not null && Key(c.TrackName) == wantedTitle)
                    .Where(c => c.ArtistName is not null && Key(c.ArtistName) == wantedArtist)
                    .Where(c => Math.Abs(c.Duration - variant.DurationSeconds) <= DurationToleranceSeconds)
                    .Where(c => c.Instrumental || HasText(c.SyncedLyrics) || HasText(c.PlainLyrics))
                    .OrderByDescending(c => HasText(c.SyncedLyrics))
                    .ThenBy(c => Math.Abs(c.Duration - variant.DurationSeconds))
                    .FirstOrDefault();
            }

            var result = candidate switch
            {
                { Instrumental: true } => LyricsLookupResult.Instrumental,
                { } c when HasText(c.SyncedLyrics) => new LyricsLookupResult(LyricsLookupStatus.Found, c.SyncedLyrics, true),
                { } c when HasText(c.PlainLyrics) => new LyricsLookupResult(LyricsLookupStatus.Found, c.PlainLyrics, false),
                _ => LyricsLookupResult.NotFound,
            };

            if (result.Status != LyricsLookupStatus.NotFound)
                return result;
        }

        return LyricsLookupResult.NotFound;
    }

    private string Root => options.Value.BaseUrl.TrimEnd('/');

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string Key(string value) =>
        Normalize.Key(value).Replace("'", string.Empty).Replace("\u2019", string.Empty);

    private sealed record LrclibRecord(
        string? TrackName,
        string? ArtistName,
        double Duration,
        bool Instrumental,
        string? PlainLyrics,
        string? SyncedLyrics);
}

public enum LyricsLookupStatus
{
    Found,
    NotFound,
    Instrumental,
}

public record LyricsLookupResult(LyricsLookupStatus Status, string? Text, bool Synced)
{
    public static readonly LyricsLookupResult NotFound = new(LyricsLookupStatus.NotFound, null, false);
    public static readonly LyricsLookupResult Instrumental = new(LyricsLookupStatus.Instrumental, null, false);
}

public record LyricsQuery(string Title, string Artist, string? Album, int DurationSeconds);
