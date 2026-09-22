// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { SPECTRUM_BANDS } from "./spectrumBands";

const BAR_PITCH = 9;
const MIN_SIDE = SPECTRUM_BANDS;
const MAX_SIDE = 128;

/** Возвращает ноль для скрытого элемента, чтобы визуализатор вообще не запускался. */
export function barCount(width: number): number {
  if (width <= 0) return 0;

  const side = Math.min(MAX_SIDE, Math.max(MIN_SIDE, Math.floor(width / BAR_PITCH / 2)));
  return side * 2;
}

/** Дробная позиция в спектре: полосы интерполируются, а не выбрасываются. */
export function bandPositions(bars: number, mirrored: boolean): number[] {
  if (bars <= 0) return [];

  const side = mirrored ? Math.ceil(bars / 2) : bars;
  const denominator = Math.max(1, side - 1);

  return Array.from({ length: bars }, (_, index) => {
    const position = mirrored ? Math.min(index, bars - 1 - index) : index;
    return (position / denominator) * (SPECTRUM_BANDS - 1);
  });
}

/** Линейная интерполяция между соседними полосами с зажимом на краях. */
export function sampleAt(levels: ArrayLike<number>, position: number): number {
  if (levels.length === 0) return 0;

  const clamped = Math.max(0, Math.min(levels.length - 1, position));
  const left = Math.floor(clamped);
  const right = Math.min(levels.length - 1, left + 1);
  const mix = clamped - left;

  return levels[left] * (1 - mix) + levels[right] * mix;
}
