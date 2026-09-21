// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

/// <summary>
/// Сколько раз один трек шёл сразу за другим. Направленное смежное следование — сигнал,
/// которого у остальной коллаборативной части нет: ко-встречаемость в <c>build-pairs.sql</c>
/// ненаправленная и оконная, она знает «звучали в одной сессии», но не «именно после».
/// <para>
/// Граф общий, а не по слушателям. У musik вопрос не стоял — там один пользователь; в сервисе
/// же персональный граф был бы безнадёжно разрежен, а общий как раз накапливает то, чему
/// переходы и должны учить: какие стыки звучат естественно.
/// </para>
/// </summary>
public class TrackTransition
{
    public Guid FromTrackId { get; set; }
    public Track? FromTrack { get; set; }

    public Guid ToTrackId { get; set; }
    public Track? ToTrack { get; set; }

    public double Weight { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
