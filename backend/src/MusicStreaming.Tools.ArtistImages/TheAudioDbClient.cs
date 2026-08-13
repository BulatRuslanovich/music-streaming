using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Tools.ArtistImages;

public enum ArtistLookupStatus
{
    Found,
    NotFound,
    Ambiguous,
}

public record ArtistLookupResult(ArtistLookupStatus Status, string? ImageUrl);

public class TheAudioDbClient(HttpClient http, IOptions<TheAudioDbOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<ArtistLookupResult> LookupAsync(string artistName, CancellationToken ct)
    {
        var baseUrl = options.Value.BaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/{options.Value.ApiKey}/search.php?s={Uri.EscapeDataString(artistName)}";

        var response = await http.GetFromJsonAsync<SearchResponse>(url, JsonOptions, ct);

        var key = Normalize.Key(artistName);
        var matches = (response?.Artists ?? [])
            .Where(a => a.StrArtist is not null && Normalize.Key(a.StrArtist) == key)
            .ToList();

        if (matches.Count == 0)
            return new ArtistLookupResult(ArtistLookupStatus.NotFound, null);

        if (matches.Count > 1)
            return new ArtistLookupResult(ArtistLookupStatus.Ambiguous, null);

        var imageUrl = matches[0].StrArtistThumb ?? matches[0].StrArtistFanart;
        return imageUrl is null
            ? new ArtistLookupResult(ArtistLookupStatus.NotFound, null)
            : new ArtistLookupResult(ArtistLookupStatus.Found, imageUrl);
    }

    private sealed record SearchResponse(List<ArtistResult>? Artists);

    private sealed record ArtistResult(string? StrArtist, string? StrArtistThumb, string? StrArtistFanart);
}
