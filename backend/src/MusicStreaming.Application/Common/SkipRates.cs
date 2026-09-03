// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

namespace MusicStreaming.Application.Common;

/// <summary>
/// Доля пропусков среди прослушиваний, которые чем-то закончились.
/// </summary>
/// <remarks>
/// Считается всегда после материализации счётчиков, а не выражением в SQL: знаменатель законно
/// бывает нулём — у нового пользователя, у трека, который ещё никто не открывал, у пустого периода.
/// Один общий вход гарантирует, что обзор, список пользователей и карточка одного слушателя
/// понимают «пропуски» одинаково.
/// </remarks>
public static class SkipRates
{
    /// <summary>Доля пропусков от 0 до 1. Без событий — 0, а не деление на ноль и не NaN.</summary>
    public static double Of(int completed, int skipped)
    {
        var finished = completed + skipped;

        return finished == 0 ? 0 : skipped / (double)finished;
    }
}
