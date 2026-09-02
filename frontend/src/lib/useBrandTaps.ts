// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useCallback, useRef } from "react";
import { nextStreak, type TapStreak } from "./tapStreak";

/**
 * Штрихов волны в знаке ровно семь — число берётся из самого знака, а не назначено снаружи.
 * Окно короткое: это жест, а не счётчик кликов по логотипу за сессию.
 */
const TAPS = 7;

const WINDOW_MS = 500;

export function useBrandTaps(onReach: () => void): () => void {
  const streak = useRef<TapStreak | null>(null);

  return useCallback(() => {
    const now = Date.now();
    const count = nextStreak(streak.current, now, WINDOW_MS);

    if (count >= TAPS) {
      streak.current = null;
      onReach();
      return;
    }

    streak.current = { count, at: now };
  }, [onReach]);
}
