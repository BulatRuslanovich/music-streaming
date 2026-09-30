// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MusicStreaming.Application.Abstractions;
using MusicStreaming.Application.Common;
using MusicStreaming.Application.Options;
using MusicStreaming.Domain.Common;

namespace MusicStreaming.Infrastructure.Integrations;

public class TheAudioDbClient(
    HttpClient http,
    IHttpClientFactory httpClientFactory,
    IOptions<AudioDbOptions> options) : IArtistImageProvider
{
    public const string ImageClientName = "artist-image-content";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<ArtistImageLookupResult> LookupAsync(string artistName, CancellationToken ct)
    {
        var baseUrl = options.Value.BaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/{options.Value.ApiKey}/search.php?s={Uri.EscapeDataString(artistName)}";

        var response = await http.GetFromJsonAsync<SearchResponse>(url, JsonOptions, ct);

        var key = Normalize.Key(artistName);
        var matches = (response?.Artists ?? [])
            .Where(artist => artist.StrArtist is not null && Normalize.Key(artist.StrArtist) == key)
            .ToList();

        if (matches.Count == 0)
            return ArtistImageLookupResult.NotFound;

        if (matches.Count > 1)
            return ArtistImageLookupResult.Ambiguous;

        var imageUrl = matches[0].StrArtistThumb ?? matches[0].StrArtistFanart;
        if (imageUrl is null)
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

    private sealed record SearchResponse(List<ArtistResult>? Artists);

    private sealed record ArtistResult(string? StrArtist, string? StrArtistThumb, string? StrArtistFanart);
}
