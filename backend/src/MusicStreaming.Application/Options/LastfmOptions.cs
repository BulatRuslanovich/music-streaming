// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace MusicStreaming.Application.Options;

public class LastfmOptions
{
    public const string SectionName = "Lastfm";

    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string PublicUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiSecret);

    // Секция целиком необязательна — обязательных полей здесь нет. Но полузаполненная секция
    // тише всего ломается уже в браузере, на редиректе Last.fm, поэтому она проверяется на старте.
    public static OptionsBuilder<LastfmOptions> Validated(OptionsBuilder<LastfmOptions> builder) => builder
        .Validate(
            o => string.IsNullOrWhiteSpace(o.ApiKey) == string.IsNullOrWhiteSpace(o.ApiSecret),
            "Lastfm:ApiKey and Lastfm:ApiSecret must be set together, or both left empty.")
        .Validate(
            o => string.IsNullOrWhiteSpace(o.PublicUrl)
                 || Uri.TryCreate(o.PublicUrl, UriKind.Absolute, out _),
            "Lastfm:PublicUrl must be an absolute URL — it is the origin of the OAuth callback.");
}
