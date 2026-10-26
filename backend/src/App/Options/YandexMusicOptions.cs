// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using Microsoft.Extensions.Options;

namespace App.Options;

// Экспериментальный импорт из Яндекс Музыки. Токен сюда не входит: он живёт в браузере админа
// и приходит с каждым запросом. Здесь — то, что меняется, только если Яндекс поменяет API.
public class YandexMusicOptions
{
    public const string SectionName = "YandexMusic";

    public string BaseUrl { get; set; } = "https://api.music.yandex.net";

    // Клиент, от имени которого подписан запрос ссылки на файл; ключ подписи — его.
    public string Client { get; set; } = "YandexMusicWebNext/1.0.0";

    public string SignKey { get; set; } = "7tvSmFbyf5hJnIHhCimDDD";

    public static OptionsBuilder<YandexMusicOptions> Validated(OptionsBuilder<YandexMusicOptions> builder) => builder
        .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "YandexMusic:BaseUrl is required.")
        .Validate(o => !string.IsNullOrWhiteSpace(o.Client), "YandexMusic:Client is required.")
        .Validate(o => !string.IsNullOrWhiteSpace(o.SignKey), "YandexMusic:SignKey is required.");
}
