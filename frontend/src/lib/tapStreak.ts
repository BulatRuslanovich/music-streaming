// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

export interface TapStreak {
  count: number;
  at: number;
}

/**
 * Длина серии после очередного касания. Серия рвётся паузой, а не промахом: считать нужно
 * жест — семь щелчков подряд, — а не накопленные за сессию клики по одному и тому же месту.
 */
export function nextStreak(previous: TapStreak | null, now: number, windowMs: number): number {
  if (previous === null) return 1;
  if (now - previous.at > windowMs) return 1;

  return previous.count + 1;
}
