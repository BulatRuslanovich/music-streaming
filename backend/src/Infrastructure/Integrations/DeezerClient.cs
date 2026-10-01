// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using System.Text.Json;
using App.Abstractions;
using App.Common;
using Domain.Common;

namespace Infrastructure.Integrations;

public class DeezerClient(HttpClient http, IHttpClientFactory httpClientFactory) : IArtistImageProvider
{
    public const string ImageClientName = "artist-image-content";

    private const string SearchUrl = "https://api.deezer.com/search/artist";
    private const string MissingPictureMarker = "/images/artist//";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<ArtistImageLookupResult> LookupAsync(string artistName, CancellationToken ct)
    {
        var response = await http.GetFromJsonAsync<SearchResponse>(
            $"{SearchUrl}?q={Uri.EscapeDataString(artistName)}", JsonOptions, ct);
        if (response?.Error is { } error)
            throw new HttpRequestException($"Deezer refused the search: {error.Message}");

        var key = Normalize.Key(artistName);
        var imageUrl = response?.Data?
            .FirstOrDefault(artist => artist.Name is not null && Normalize.Key(artist.Name) == key)?
            .PictureXl;

        if (imageUrl is null || imageUrl.Contains(MissingPictureMarker, StringComparison.Ordinal))
            return ArtistImageLookupResult.NotFound;

        const long maxBytes = UploadLimits.ImageBytes;
        var client = httpClientFactory.CreateClient(ImageClientName);
        using var image = await client.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        image.EnsureSuccessStatusCode();

        if (image.Content.Headers.ContentLength is { } length && length > maxBytes)
            throw new HttpRequestException($"The artist image exceeds the {maxBytes} byte limit.");

        await using var input = await image.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();

        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > maxBytes)
                throw new HttpRequestException($"The artist image exceeds the {maxBytes} byte limit.");

            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return new ArtistImageLookupResult(ArtistImageLookupStatus.Found, output.ToArray());
    }

    private sealed record SearchResponse(List<ArtistResult>? Data, SearchError? Error);

    private sealed record SearchError(string? Message);

    private sealed record ArtistResult(string? Name, string? PictureXl);
}
