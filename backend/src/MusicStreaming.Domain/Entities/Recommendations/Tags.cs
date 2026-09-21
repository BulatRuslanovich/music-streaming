// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

/// <summary>
/// Как теги артиста доносятся до его треков. Число одно на систему: по нему строится вектор
/// схожести и по нему же трек попадает на страницу тега — разойдись эти два правила, полка и
/// раздел «Теги» стали бы спорить о том, что вообще считается этим тегом.
/// </summary>
public static class TagWeights
{
    /// <summary>
    /// Доля веса, с которой тег артиста засчитывается его треку. У домашней библиотеки
    /// <c>track.getTopTags</c> пуст чаще, чем полон, поэтому без наследования от артиста
    /// размечена была бы едва десятая часть треков.
    /// </summary>
    public const double ArtistShare = 0.6;

    /// <summary>
    /// Сколько тегов сохраняется на артиста или трек. Число определяет форму вектора схожести,
    /// поэтому оно такое же одно на систему, как <see cref="ArtistShare"/>.
    /// </summary>
    public const int MaxPerEntity = 12;

    /// <summary>Ниже этого веса тег не несёт информации и только раздувает вектор.</summary>
    public const double Minimum = 0.05;
}

/// <summary>
/// Тег из внешнего каталога. У трека в библиотеке ровно один жанр, поэтому контентная схожесть
/// двух записей разных исполнителей держится почти на нём одном; веса тегов дают ту же схожесть
/// в виде вектора, а не одного ярлыка.
/// </summary>
public class ArtistTag
{
    public Guid ArtistId { get; set; }
    public Artist? Artist { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Вес тега, 0..1.</summary>
    public double Weight { get; set; }
}

public class TrackTag
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Weight { get; set; }
}
