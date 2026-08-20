// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MusicStreaming.Application.Dtos;
using Xunit;

namespace MusicStreaming.IntegrationTests;

internal record TestUploadFile(string FileName, string? ContentType, byte[] Content);

internal static class TrackUploadTestClient
{
    public static async Task<UploadResultDto> UploadAsync(
        HttpClient client,
        IReadOnlyList<TestUploadFile> files,
        JsonSerializerOptions json)
    {
        var uploaded = new List<TrackDto>();
        var failed = new List<UploadFailureDto>();

        foreach (var file in files)
        {
            var result = await UploadOneAsync(client, file, json);
            uploaded.AddRange(result.Uploaded);
            failed.AddRange(result.Failed);
        }

        return new UploadResultDto(uploaded, failed);
    }

    public static async Task<UploadResultDto> UploadOneAsync(
        HttpClient client,
        TestUploadFile file,
        JsonSerializerOptions json)
    {
        using var content = new ByteArrayContent(file.Content);
        if (file.ContentType is not null)
            content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
        content.Headers.Add("X-File-Name", Uri.EscapeDataString(file.FileName));

        var response = await client.PostAsync("/api/tracks/upload", content, Cancel.Token);

        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest,
            $"unexpected status {response.StatusCode}: {await response.Content.ReadAsStringAsync(Cancel.Token)}");

        return (await response.Content.ReadFromJsonAsync<UploadResultDto>(json, Cancel.Token))!;
    }
}
