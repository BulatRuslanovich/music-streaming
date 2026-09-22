// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { formatDuration } from "@/lib/format";
import { toggleRemainingTime, useRemainingTime } from "@/lib/useRemainingTime";
import { usePlayerActions, usePlayerProgress } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";

/**
 * Состояние полосы перемотки — общее у плеера в футере и у полноэкранного.
 *
 * Вынесено сюда, а не в общий компонент: вёрстка у них разная по существу. В футере часы стоят
 * по краям полосы и есть индикатор буфера, на полном экране полоса идёт сверху, а часы — строкой
 * под ней. Совпадает ровно то, что ниже, включая переключатель «прошло / осталось».
 *
 * `fallbackDuration` закрывает окно до `loadedmetadata`, когда декодированной длительности ещё
 * нет, а в метаданных трека она уже есть.
 */
export function usePlaybackProgress(fallbackDuration: number) {
  const { position, duration, buffered } = usePlayerProgress();
  const { seek } = usePlayerActions();
  const showRemaining = useRemainingTime();
  const t = useT();

  const total = duration || fallbackDuration;

  return {
    position,
    total,
    bufferedPercent: total > 0 ? Math.min(100, (buffered / total) * 100) : 0,
    seek,
    seekLabel: t("player.seek"),
    toggleRemainingTime,
    toggleRemainingLabel: t("player.toggleRemaining"),
    endLabel: showRemaining
      ? `-${formatDuration(Math.max(0, total - position))}`
      : formatDuration(total),
  };
}
