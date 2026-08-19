// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Tools.ArtistImages;

public class TheAudioDbOptions
{
    public const string SectionName = "AudioDb";

    public string ApiKey { get; set; } = "2";
    public string BaseUrl { get; set; } = "https://www.theaudiodb.com/api/v1/json";
    public int RequestDelayMs { get; set; } = 1000;
}
