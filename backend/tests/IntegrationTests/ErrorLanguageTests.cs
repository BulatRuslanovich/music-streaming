// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace IntegrationTests;

[Collection(nameof(RecommendationApiCollection))]
public class ErrorLanguageTests(RecommendationApiFixture fixture)
{
    [Theory]
    [InlineData("ru-RU,ru;q=0.9,en;q=0.8", "ru", "Волна трека ещё не готова.")]
    [InlineData("de-DE,de;q=0.9,ru;q=0.5", "ru", "Волна трека ещё не готова.")]
    [InlineData("en;q=0.3, ru;q=0.8", "ru", "Волна трека ещё не готова.")]
    [InlineData("en-US,en;q=0.9,ru;q=0.8", "en", "The waveform is not ready yet.")]
    [InlineData("fr, de", "en", "The waveform is not ready yet.")]
    [InlineData(null, "en", "The waveform is not ready yet.")]
    public async Task Errors_come_back_in_the_language_the_client_asked_for(
        string? acceptLanguage, string language, string detail)
    {
        Assert.SkipUnless(fixture.DockerAvailable, fixture.SkipReason);

        var (library, client) = await fixture.SeedAndSignInAsync(artistCount: 1, tracksPerArtist: 1);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/tracks/{library.Track(0)}/waveform");
        if (acceptLanguage is not null)
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);

        var response = await client.SendAsync(request, Cancel.Token);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Cancel.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(detail, problem?.Detail);
        Assert.Equal([language], response.Content.Headers.ContentLanguage);
    }
}
