// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

/// <summary>How an artist's tags reach that artist's tracks.</summary>
/// <remarks>
/// Число одно на систему: по нему строится вектор схожести и по нему же трек попадает на страницу
/// тега — разойдись эти два правила, полка и раздел «Теги» стали бы спорить о том, что вообще
/// считается этим тегом.
/// </remarks>
public static class TagWeights
{
    /// <summary>Fraction of an artist tag's weight that carries over to the artist's tracks.</summary>
    /// <remarks>
    /// У домашней библиотеки <c>track.getTopTags</c> пуст чаще, чем полон, поэтому без наследования
    /// от артиста размечена была бы едва десятая часть треков.
    /// </remarks>
    public const double ArtistShare = 0.6;

    /// <summary>
    /// How many tags are kept per artist or track.
    /// </summary>
    /// <remarks>
    /// Число определяет форму вектора схожести, поэтому оно такое же одно на систему,
    /// как <see cref="ArtistShare"/>.
    /// </remarks>
    public const int MaxPerEntity = 12;

    /// <summary>Below this weight a tag carries no information and only inflates the vector.</summary>
    public const double Minimum = 0.05;
}

/// <summary>
/// A tag from an external catalogue.
/// </summary>
/// <remarks>
/// У трека в библиотеке ровно один жанр, поэтому контентная схожесть двух записей разных
/// исполнителей держится почти на нём одном; веса тегов дают ту же схожесть в виде вектора,
/// а не одного ярлыка.
/// </remarks>
public class ArtistTag
{
    public Guid ArtistId { get; set; }
    public Artist? Artist { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Tag weight, 0..1.</summary>
    public double Weight { get; set; }
}

public class TrackTag
{
    public Guid TrackId { get; set; }
    public Track? Track { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Weight { get; set; }
}
