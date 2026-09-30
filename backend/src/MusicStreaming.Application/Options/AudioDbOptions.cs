// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

public class AudioDbOptions
{
    public const string SectionName = "AudioDb";

    public string ApiKey { get; set; } = "123";
    public string BaseUrl { get; set; } = "https://www.theaudiodb.com/api/v1/json";

    public static OptionsBuilder<AudioDbOptions> Validated(OptionsBuilder<AudioDbOptions> builder) => builder
        .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "AudioDb:ApiKey is required.")
        .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "AudioDb:BaseUrl is required.");
}
