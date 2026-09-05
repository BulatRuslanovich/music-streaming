// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useFormat } from "@/lib/useFormat";

/**
 * Изменение времени прослушивания к прошлому периоду.
 *
 * Общий на статистику и итоги месяца: подпись у них разная, а правила показа одни — рост
 * янтарём, спад нейтральным, минус настоящий (U+2212), цифры моноширинные, чтобы столбик
 * не дёргался при смене периода.
 */
export function Delta({
  percent,
  previousSeconds,
  caption,
}: {
  percent: number;
  previousSeconds: number;
  caption: string;
}) {
  const format = useFormat();
  const grew = percent >= 0;

  return (
    <div className="flex flex-col items-end gap-0.5 text-right">
      <span
        className={
          grew
            ? "text-section font-semibold text-primary tabular-nums"
            : "text-section font-semibold text-muted-foreground tabular-nums"
        }
      >
        {grew ? "+" : "−"}
        {Math.abs(percent)}%
      </span>
      <span className="text-2xs text-faint">{caption}</span>
      <span className="text-2xs text-faint tabular-nums">
        {format.totalDuration(previousSeconds)}
      </span>
    </div>
  );
}
