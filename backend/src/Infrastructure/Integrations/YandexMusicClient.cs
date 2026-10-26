// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using App.Common;
using App.Options;

namespace Infrastructure.Integrations;

// API Яндекс Музыки от имени владельца OAuth-токена. Ответы разбираются вручную: id в них бывают
// и строками, и числами, а лишние поля меняются от версии к версии.
public class YandexMusicClient(HttpClient http, IHttpClientFactory httpClientFactory, IOptions<YandexMusicOptions> options)
{
    public const string DownloadClientName = "yandex-music-download";

    private const int TracksPerRequest = 100;
    private const string CoverSize = "1000x1000";
    private const string ThumbnailSize = "200x200";

    public async Task<YandexSearchResult> SearchAsync(string token, string text, CancellationToken ct)
    {
        var result = await GetAsync(token, $"/search?text={Uri.EscapeDataString(text)}&type=all&page=0&nocorrect=false", ct);

        return new YandexSearchResult(
            [.. Results(result, "tracks").Select(ReadTrack)],
            [.. Results(result, "albums").Select(ReadAlbum)]);

        static IEnumerable<JsonElement> Results(JsonElement result, string section) =>
            result.TryGetProperty(section, out var block) && block.TryGetProperty("results", out var items)
                && items.ValueKind == JsonValueKind.Array
                ? items.EnumerateArray()
                : [];
    }

    // Лайки идут от новых к старым — в том же порядке и импортируются.
    public async Task<IReadOnlyList<string>> LikedTrackIdsAsync(string token, CancellationToken ct)
    {
        var status = await GetAsync(token, "/account/status", ct);
        var uid = Text(status.GetPropertyOrDefault("account"), "uid")
            ?? throw new ValidationException("Yandex Music rejected the token.");

        var likes = await GetAsync(token, $"/users/{uid}/likes/tracks", ct);
        var tracks = likes.GetPropertyOrDefault("library").GetPropertyOrDefault("tracks");

        return tracks.ValueKind == JsonValueKind.Array
            ? [.. tracks.EnumerateArray().Select(track => Text(track, "id")).OfType<string>()]
            : [];
    }

    public async Task<IReadOnlyList<YandexTrack>> TracksAsync(string token, IReadOnlyList<string> ids, CancellationToken ct)
    {
        var tracks = new List<YandexTrack>(ids.Count);

        foreach (var chunk in ids.Chunk(TracksPerRequest))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Url("/tracks"))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["track-ids"] = string.Join(',', chunk),
                    ["with-positions"] = "true",
                }),
            };

            var result = await SendAsync(token, request, ct);
            if (result.ValueKind == JsonValueKind.Array)
                tracks.AddRange(result.EnumerateArray().Select(ReadTrack));
        }

        return tracks;
    }

    public async Task<YandexAlbumTracks> AlbumAsync(string token, string albumId, CancellationToken ct)
    {
        var result = await GetAsync(token, $"/albums/{Uri.EscapeDataString(albumId)}/with-tracks", ct);
        var album = ReadAlbum(result);

        // Треки альбома разложены по дискам: volumes — массив массивов.
        var volumes = result.GetPropertyOrDefault("volumes");
        IReadOnlyList<YandexTrack> tracks = volumes.ValueKind == JsonValueKind.Array
            ? [.. volumes.EnumerateArray()
                .Where(volume => volume.ValueKind == JsonValueKind.Array)
                .SelectMany(volume => volume.EnumerateArray())
                .Select(ReadTrack)]
            : [];

        return new YandexAlbumTracks(album, tracks);
    }

    // Прямая ссылка на файл. Запрос подписан так же, как в веб-клиенте: HMAC-SHA256 от склеенных
    // параметров. null — Яндекс не отдал ни один из запрошенных кодеков.
    public async Task<YandexFileInfo?> FileInfoAsync(
        string token, string trackId, string quality, IReadOnlyList<string> codecs, CancellationToken ct)
    {
        const string transport = "raw";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sign = Sign(options.Value.SignKey, $"{timestamp}{trackId}{quality}{string.Concat(codecs)}{transport}");

        JsonElement result;
        try
        {
            result = await GetAsync(
                token,
                $"/get-file-info?ts={timestamp}&trackId={Uri.EscapeDataString(trackId)}&quality={quality}"
                + $"&codecs={string.Join(',', codecs)}&transports={transport}&sign={Uri.EscapeDataString(sign)}",
                ct);
        }
        catch (ValidationException) when (quality != "hq")
        {
            // Lossless недоступен по подписке или для трека — пусть вызывающий попросит MP3.
            return null;
        }

        var info = result.GetPropertyOrDefault("downloadInfo");
        var url = Text(info, "url")
                  ?? (info.TryGetProperty("urls", out var urls) && urls.ValueKind == JsonValueKind.Array
                      ? urls.EnumerateArray().Select(u => u.GetString()).FirstOrDefault(u => !string.IsNullOrEmpty(u))
                      : null);
        var codec = Text(info, "codec");

        // Другие транспорты (encraw) отдают зашифрованный поток — такой файл не сохранить.
        if (url is null || codec is null || (Text(info, "transport") ?? transport) != transport)
            return null;

        return new YandexFileInfo(url, codec);
    }

    public async Task DownloadAsync(string url, string targetPath, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(DownloadClientName);
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
            throw new ValidationException("Yandex Music did not answer ({0}).", (int)response.StatusCode);

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(targetPath);
        await source.CopyToAsync(target, ct);
    }

    public async Task<byte[]?> CoverAsync(string? coverUri, CancellationToken ct)
    {
        if (CoverUrl(coverUri, CoverSize) is not { } url)
            return null;

        try
        {
            var client = httpClientFactory.CreateClient(DownloadClientName);
            return await client.GetByteArrayAsync(url, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    // Справочник жанров: в треках только код («rusrap»), а показывать нужно название («Русский рэп»).
    public async Task<IReadOnlyDictionary<string, string>> GenreTitlesAsync(string token, CancellationToken ct)
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        var result = await GetAsync(token, "/genres", ct);

        if (result.ValueKind == JsonValueKind.Array)
            Collect(result);

        return titles;

        void Collect(JsonElement genres)
        {
            foreach (var genre in genres.EnumerateArray())
            {
                if (Text(genre, "id") is { } id && Text(genre, "title") is { } title)
                    titles.TryAdd(id, title);

                if (genre.TryGetProperty("subGenres", out var sub) && sub.ValueKind == JsonValueKind.Array)
                    Collect(sub);
            }
        }
    }

    public static string? ThumbnailUrl(string? coverUri) => CoverUrl(coverUri, ThumbnailSize);

    // Адрес обложки приходит без схемы и с «%%» вместо размера.
    private static string? CoverUrl(string? coverUri, string size) =>
        string.IsNullOrWhiteSpace(coverUri)
            ? null
            : $"https://{coverUri.Replace("%%", size)}";

    internal static string Sign(string key, string payload)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash).TrimEnd('=');
    }

    private Task<JsonElement> GetAsync(string token, string path, CancellationToken ct) =>
        SendAsync(token, new HttpRequestMessage(HttpMethod.Get, Url(path)), ct);

    private async Task<JsonElement> SendAsync(string token, HttpRequestMessage request, CancellationToken ct)
    {
        using (request)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);
            request.Headers.Add("X-Yandex-Music-Client", options.Value.Client);
            request.Headers.Add("X-Yandex-Music-Without-Invocation-Info", "1");

            using var response = await http.SendAsync(request, ct);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new ValidationException("Yandex Music rejected the token.");

            if (!response.IsSuccessStatusCode)
                throw new ValidationException("Yandex Music did not answer ({0}).", (int)response.StatusCode);

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: ct);

            // Обычно ответ обёрнут в { result: … }, но списки (например, /tracks) приходят голым массивом.
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out var result)
                ? result.Clone()
                : root.Clone();
        }
    }

    private string Url(string path) => $"{options.Value.BaseUrl.TrimEnd('/')}{path}";

    private static YandexTrack ReadTrack(JsonElement track)
    {
        var albums = track.GetPropertyOrDefault("albums");
        var album = albums.ValueKind == JsonValueKind.Array && albums.GetArrayLength() > 0
            ? ReadAlbum(albums[0])
            : null;

        return new YandexTrack(
            Id: Text(track, "id") ?? string.Empty,
            Title: WithVersion(Text(track, "title") ?? string.Empty, Text(track, "version")),
            Artists: Names(track),
            Album: album,
            CoverUri: Text(track, "coverUri") ?? album?.CoverUri,
            DurationSeconds: track.TryGetProperty("durationMs", out var ms) && ms.TryGetInt64(out var value)
                ? (int)Math.Round(value / 1000.0)
                : 0,
            // Поля нет — трек доступен; false — снят с сервиса или не по подписке.
            Available: !track.TryGetProperty("available", out var available) || available.ValueKind != JsonValueKind.False);
    }

    private static YandexAlbum ReadAlbum(JsonElement album)
    {
        var position = album.GetPropertyOrDefault("trackPosition");

        return new YandexAlbum(
            Id: Text(album, "id") ?? string.Empty,
            Title: WithVersion(Text(album, "title") ?? string.Empty, Text(album, "version")),
            Artists: Names(album),
            Year: Number(album, "year"),
            Genre: Text(album, "genre"),
            CoverUri: Text(album, "coverUri"),
            TrackCount: Number(album, "trackCount"),
            TrackNumber: Number(position, "index"),
            DiscNumber: Number(position, "volume"));
    }

    // Версия («Remix», «Live») — часть названия: без неё два разных трека выглядят одинаково.
    private static string WithVersion(string title, string? version) =>
        string.IsNullOrWhiteSpace(version) || title.Contains(version, StringComparison.OrdinalIgnoreCase)
            ? title
            : $"{title} ({version.Trim()})";

    private static IReadOnlyList<string> Names(JsonElement owner) =>
        owner.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array
            ? [.. artists.EnumerateArray().Select(artist => Text(artist, "name")).OfType<string>()]
            : [];

    private static string? Text(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return null;

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static int? Number(JsonElement element, string name) =>
        int.TryParse(Text(element, name), out var value) && value > 0 ? value : null;
}

file static class JsonElementExtensions
{
    public static JsonElement GetPropertyOrDefault(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
}

public record YandexTrack(
    string Id,
    string Title,
    IReadOnlyList<string> Artists,
    YandexAlbum? Album,
    string? CoverUri,
    int DurationSeconds,
    bool Available);

public record YandexAlbum(
    string Id,
    string Title,
    IReadOnlyList<string> Artists,
    int? Year,
    string? Genre,
    string? CoverUri,
    int? TrackCount,
    int? TrackNumber,
    int? DiscNumber);

public record YandexAlbumTracks(YandexAlbum Album, IReadOnlyList<YandexTrack> Tracks);

public record YandexSearchResult(IReadOnlyList<YandexTrack> Tracks, IReadOnlyList<YandexAlbum> Albums);

public record YandexFileInfo(string Url, string Codec);
