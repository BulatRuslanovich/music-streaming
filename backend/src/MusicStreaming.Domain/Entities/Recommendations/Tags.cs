// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

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
