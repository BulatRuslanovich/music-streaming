// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Infrastructure.Integrations;

/// <summary>
/// Теги Last.fm (<c>artist.getTopTags</c> / <c>track.getTopTags</c>). Это единственные вызовы
/// каталога, которым не нужна подпись — только ключ, поэтому они живут отдельно от скробблинга.
/// </summary>
public class LastfmTagProvider(
    HttpClient http,
    IOptions<LastfmOptions> options,
    IOptions<TagEnrichmentOptions> tagOptions,
    ILogger<LastfmTagProvider> logger) : IMusicTagProvider
{
    private const string ApiRoot = "https://ws.audioscrobbler.com/2.0/";
    private const int MaxTagNameLength = 100;

    // Last.fm полон коллекционных тегов: они говорят о слушателе, а не о звуке.
    private static readonly HashSet<string> Junk =
    [
        "seen live", "favorites", "favourites", "favorite songs", "favourite songs",
        "albums i own", "own it", "my music", "spotify", "under 2000 listeners",
        "beautiful", "awesome", "amazing", "love", "loved", "best", "good", "cool",
        "usa", "uk", "british", "american", "german", "french", "russian", "swedish",
        "male vocalists", "female vocalists", "male vocalist", "female vocalist",
    ];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public Task<IReadOnlyList<ProviderTag>> ArtistTagsAsync(string artistName, CancellationToken ct) =>
        FetchAsync("artist.gettoptags", new Dictionary<string, string> { ["artist"] = artistName }, ct);

    public Task<IReadOnlyList<ProviderTag>> TrackTagsAsync(
        string artistName, string title, CancellationToken ct) =>
        FetchAsync(
            "track.gettoptags",
            new Dictionary<string, string> { ["artist"] = artistName, ["track"] = title },
            ct);

    private async Task<IReadOnlyList<ProviderTag>> FetchAsync(
        string method, Dictionary<string, string> parameters, CancellationToken ct)
    {
        if (!IsConfigured)
            return [];

        var query = string.Join('&', parameters
            .Concat(new Dictionary<string, string>
            {
                ["method"] = method,
                ["api_key"] = options.Value.ApiKey,
                ["autocorrect"] = "1",
                ["format"] = "json",
            })
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        JsonElement body;
        try
        {
            var response = await http.GetAsync($"{ApiRoot}?{query}", ct);
            body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or NotSupportedException && !ct.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Last.fm tag lookup ({Method}) failed", method);
            return [];
        }

        // Не найдено — обычный ответ, а не сбой: у половины домашней библиотеки тегов не будет.
        if (body.ValueKind != JsonValueKind.Object || body.TryGetProperty("error", out _))
            return [];

        return Parse(body);
    }

    private IReadOnlyList<ProviderTag> Parse(JsonElement body)
    {
        if (!body.TryGetProperty("toptags", out var container)
            || !container.TryGetProperty("tag", out var tags)
            || tags.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var parsed = new List<ProviderTag>();

        foreach (var tag in tags.EnumerateArray())
        {
            if (!tag.TryGetProperty("name", out var nameValue))
                continue;

            var name = Normalize(nameValue.GetString());
            if (name is null || Junk.Contains(name))
                continue;

            // count приходит в 0..100 и иногда строкой.
            var count = tag.TryGetProperty("count", out var countValue)
                ? countValue.ValueKind switch
                {
                    JsonValueKind.Number => countValue.GetDouble(),
                    JsonValueKind.String => double.TryParse(countValue.GetString(), out var text) ? text : 0,
                    _ => 0,
                }
                : 0;

            var weight = Math.Clamp(count / 100.0, 0, 1);
            if (weight < tagOptions.Value.MinimumTagWeight)
                continue;

            parsed.Add(new ProviderTag(name, weight));

            if (parsed.Count >= tagOptions.Value.MaxTagsPerEntity)
                break;
        }

        return parsed;
    }

    private static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var name = raw.Trim().ToLowerInvariant();

        return name.Length is 0 or > MaxTagNameLength ? null : name;
    }
}
