// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace Domain.Entities.Recommendations;

public class PlaybackEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public long Sequence { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid? TrackId { get; set; }
    public Track? Track { get; set; }
    public Guid? EntityId { get; set; }
    public PlaybackEventType Type { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public int PositionSeconds { get; set; }
    public int ListenedSeconds { get; set; }
    public int DurationSeconds { get; set; }
    public Guid SessionId { get; set; }

    // Откуда запущен трек: «home:forYou», «radio:mood:sad», «album», «search»… null — неизвестно
    // (старые события, очередь, восстановленная после перезагрузки). Нужен, чтобы мерить рекомендации.
    public string? Source { get; set; }

    // Признаки ранжирования рекомендации на момент показа — только у старта с рекомендательной полки
    // (NaN — признак был неизвестен). Пара «признаки → исход» учит персональные веса.
    public float[]? Features { get; set; }
}
