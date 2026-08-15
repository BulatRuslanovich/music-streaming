using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Options;

namespace MusicStreaming.Infrastructure.Integrations;

/// <summary>
/// Обращения к Last.fm.
///
/// <para>
/// Ответ приходит с кодом 200 даже когда операция не удалась, поэтому «получилось» определяется
/// полем <c>error</c> в теле, а не статусом HTTP. Разбор здесь же переводит коды ошибок в понятия,
/// которыми оперирует очередь заданий: подождать и повторить, бросить совсем или попросить
/// пользователя подключиться заново.
/// </para>
/// </summary>
public class LastfmClient(HttpClient http, IOptions<LastfmOptions> options) : ILastfmApi
{
    private const string ApiRoot = "https://ws.audioscrobbler.com/2.0/";
    private const string AuthRoot = "https://www.last.fm/api/auth/";

    /// <summary>Коды, которые пройдут сами: сервис недоступен, временная ошибка, превышена частота.</summary>
    private static readonly HashSet<int> TransientErrors = [8, 11, 16, 29];

    /// <summary>Ключ сессии отозван или неверен — пока пользователь не подключится заново, повторять нечего.</summary>
    private static readonly HashSet<int> AuthErrors = [4, 9, 14];

    private LastfmOptions Options => options.Value;

    public bool IsConfigured => Options.IsConfigured;

    public string AuthorizeUrl(string callbackUrl) =>
        $"{AuthRoot}?api_key={Uri.EscapeDataString(Options.ApiKey)}&cb={Uri.EscapeDataString(callbackUrl)}";

    public async Task<LastfmSession> CompleteAsync(string token, CancellationToken ct = default)
    {
        var response = await SendSignedAsync(new Dictionary<string, string>
        {
            ["method"] = "auth.getSession",
            ["token"] = token,
        }, ct);

        if (!response.TryGetProperty("session", out var session))
            throw new LastfmException("Last.fm returned no session.");

        var name = session.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
        var key = session.TryGetProperty("key", out var keyValue) ? keyValue.GetString() : null;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(key))
            throw new LastfmException("Last.fm returned an incomplete session.");

        return new LastfmSession(name, key);
    }

    public async Task SendAsync(LastfmTrack track, string sessionKey, CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, string>
        {
            ["method"] = track.PlayedAt is null ? "track.updateNowPlaying" : "track.scrobble",
            ["artist"] = track.Artist,
            ["track"] = track.Title,
            ["sk"] = sessionKey,
        };

        if (!string.IsNullOrWhiteSpace(track.Album))
            parameters["album"] = track.Album;

        if (track.DurationSeconds > 0)
            parameters["duration"] = track.DurationSeconds.ToString();

        if (track.PlayedAt is { } playedAt)
            parameters["timestamp"] = playedAt.ToUnixTimeSeconds().ToString();

        await SendSignedAsync(parameters, ct);
    }

    private async Task<JsonElement> SendSignedAsync(
        Dictionary<string, string> parameters, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new LastfmException("Last.fm is not configured on this server.");

        parameters["api_key"] = Options.ApiKey;
        parameters["api_sig"] = Signature(parameters);

        // format попадает в запрос после подписи: в неё он не входит.
        parameters["format"] = "json";

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(ApiRoot, new FormUrlEncodedContent(parameters), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new LastfmException($"Last.fm is unreachable: {ex.Message}", Transient: true);
        }

        var body = await ReadJsonAsync(response, ct);

        if (body.TryGetProperty("error", out var error) && error.TryGetInt32(out var code))
        {
            var message = body.TryGetProperty("message", out var text) ? text.GetString() : null;

            throw new LastfmException(
                $"Last.fm error {code}: {message ?? "no message"}",
                Transient: TransientErrors.Contains(code),
                AuthFailure: AuthErrors.Contains(code));
        }

        // Отказ на уровне HTTP без разобранного тела: 5xx стоит повторить, 4xx — нет.
        if (!response.IsSuccessStatusCode)
        {
            throw new LastfmException(
                $"Last.fm answered with {(int)response.StatusCode}",
                Transient: (int)response.StatusCode >= 500);
        }

        return body;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new LastfmException(
                $"Last.fm answered with {(int)response.StatusCode} and an unreadable body",
                Transient: (int)response.StatusCode >= 500);
        }
    }

    /// <summary>
    /// Подпись запроса: параметры по алфавиту, склеенные как имя+значение, плюс секрет, и всё это
    /// в MD5. Алгоритм задан Last.fm — выбора здесь нет.
    /// </summary>
    private string Signature(IReadOnlyDictionary<string, string> parameters)
    {
        var builder = new StringBuilder();

        foreach (var (key, value) in parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            builder.Append(key).Append(value);

        builder.Append(Options.ApiSecret);

        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
