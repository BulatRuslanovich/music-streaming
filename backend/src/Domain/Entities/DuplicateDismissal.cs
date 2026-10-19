// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace Domain.Entities;

// Пара треков, которую админ отметил как «не дубликат»: поиск дубликатов её больше не предлагает.
// Пара хранится упорядоченной — FirstTrackId меньше SecondTrackId.
public class DuplicateDismissal
{
    public Guid FirstTrackId { get; set; }
    public Guid SecondTrackId { get; set; }
    public DateTimeOffset DismissedAt { get; set; }

    public static (Guid First, Guid Second) Ordered(Guid a, Guid b) => a.CompareTo(b) < 0 ? (a, b) : (b, a);
}
