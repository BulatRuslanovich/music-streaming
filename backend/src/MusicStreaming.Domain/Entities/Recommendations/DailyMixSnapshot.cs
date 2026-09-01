// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Domain.Entities.Recommendations;

/// <summary>
/// Плейлист дня, зафиксированный на конкретную local-дату слушателя. Рекомендации под ним
/// пересчитываются несколько раз в сутки, поэтому состав микса запоминается при первом обращении
/// и до конца дня уже не пересобирается — иначе «подборка на сегодня» менялась бы под руками.
/// </summary>
public class DailyMixSnapshot
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public DateOnly LocalDate { get; set; }
    public IReadOnlyList<Guid> TrackIds { get; set; } = [];
    public DateTimeOffset GeneratedAt { get; set; }
}
