// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

using MusicStreaming.Application.Dtos;
using MusicStreaming.Domain.Entities.Recommendations;

namespace MusicStreaming.Application.Services.Recommendations;

public static class ShelfKeys
{
    public const string ContinueListening = "continueListening";
    public const string ForYou = "forYou";
    public const string SimilarTo = "similarTo";
    public const string BecauseYouListened = "becauseYouListened";
    public const string Discover = "discover";
    public const string GenreMix = "genreMix";
    public const string NewReleases = "newReleases";
    public const string Popular = "popular";
    public const string ArtistsForYou = "artistsForYou";
    public const string AlbumsForYou = "albumsForYou";

    // Части суток — отдельные ключи, а не один с параметром: заголовок полки переводится на
    // клиенте по ключу, и «вечер» из бэкенда пришлось бы тащить строкой мимо словаря.
    public const string MorningMix = "morningMix";
    public const string DayMix = "dayMix";
    public const string EveningMix = "eveningMix";
    public const string NightMix = "nightMix";

    public static string Of(Daypart part) => part switch
    {
        Daypart.Morning => MorningMix,
        Daypart.Day => DayMix,
        Daypart.Evening => EveningMix,
        _ => NightMix,
    };

    public static Daypart? DaypartOf(string shelfKey) => BaseOf(shelfKey) switch
    {
        MorningMix => Daypart.Morning,
        DayMix => Daypart.Day,
        EveningMix => Daypart.Evening,
        NightMix => Daypart.Night,
        _ => null,
    };

    public static string Seeded(string key, Guid seed) => $"{key}:{seed}";

    /// <summary>Ключ ленты диджея. Полкой не является — им помечаются впечатления и метрики.</summary>
    public static string Dj(DjMode mode) => $"dj:{mode.ToString().ToLowerInvariant()}";

    public static string BaseOf(string shelfKey)
    {
        var separator = shelfKey.IndexOf(':');
        return separator < 0 ? shelfKey : shelfKey[..separator];
    }
}
