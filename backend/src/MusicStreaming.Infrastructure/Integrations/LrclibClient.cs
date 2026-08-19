// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Infrastructure.Integrations;

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

/// <summary>
/// Читает тексты из LRCLIB — бесплатной открытой базы, которой не нужен ключ.
/// Синхронные тексты приходят готовой LRC-строкой, то есть тем же форматом, что и правки руками,
/// поэтому разбирать их дальше умеет уже существующий <see cref="LyricsText.Parse"/>.
/// </summary>
public class LrclibClient(HttpClient http, IOptions<LrclibOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Ищет текст, пробуя запрос в исходном написании, а затем в латинском. База знает русские
    /// группы по подписи на латинице — «Король и Шут» лежит там как «Korol i Shut», — и по
    /// кириллице не находится вовсе.
    /// </summary>
    public async Task<LyricsLookupResult> LookupAsync(LyricsQuery query, CancellationToken ct)
    {
        foreach (var variant in Variants(query))
        {
            // «Инструментал» — такой же ответ по существу, как найденный текст: у трека его нет по
            // замыслу, и второй заход ничего не добавит.
            var result = await LookupOnceAsync(variant, ct);
            if (result.Status != LyricsLookupStatus.NotFound)
                return result;
        }

        return LyricsLookupResult.NotFound;
    }

    /// <summary>
    /// Написания, в которых стоит спросить базу, в порядке убывания вероятности.
    /// </summary>
    /// <remarks>
    /// Порядок взят не из головы: чаще всего латиницей подписан только исполнитель, а название
    /// остаётся кириллицей. «Лесник» лежит там именно как «Korol i Shut — Лесник», а полностью
    /// латинское «Korol i Shut — Lesnik» не находит ничего.
    ///
    /// Когда исполнитель и так на латинице, второй вариант совпадает с первым и отсеивается сам,
    /// а третий превращается в «латинское название при исходном исполнителе» — тоже нужный случай.
    /// </remarks>
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

                // Альбом идёт в том же алфавите, что и название: и то и другое — собственный текст
                // релиза, и в базе они записаны заодно.
                Album = pair.Title == query.Title || query.Album is null
                    ? query.Album
                    : Translit.ToLatin(query.Album),
            };
        }
    }

    private async Task<LyricsLookupResult> LookupOnceAsync(LyricsQuery query, CancellationToken ct)
    {
        // Точному поиску отдаётся вся четвёрка признаков, и совпадение он подбирает сам — на
        // практике мягче, чем буквально: и суффикс в названии переживает, и заметное расхождение
        // длительности. Это их база, их и правила, так что ответ принимается как есть; промах
        // приходит как 404, а не как пустое тело.
        if (await GetAsync(query, ct) is { } exact)
            return Describe(exact.ToCandidate());

        // А вот поиск по ключевым словам возвращает всё подряд — чужие каверы, лайвы, мусорные
        // записи вроде "Creep;Creep", — поэтому здесь фильтр строгий и наш собственный.
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
