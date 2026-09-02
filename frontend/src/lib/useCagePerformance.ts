// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect, useSyncExternalStore } from "react";
import { CAGE_MS, cageState, markCagePerformed, serverCageState, subscribeCage } from "./silence";
import { useIdle } from "./useIdle";

/**
 * Пауза плюс 4′33″ полного бездействия — это уже исполнение пьесы, а не простой. Считается
 * один раз на браузер: вторая тишина той же длины ничего не значит.
 *
 * `useIdle` сам отключается на грубом указателе, так что находка остаётся десктопной.
 */
export function useCagePerformance(paused: boolean): boolean {
  const state = useSyncExternalStore(subscribeCage, cageState, serverCageState);
  const idle = useIdle(CAGE_MS, paused && state === "armed");

  useEffect(() => {
    if (idle) markCagePerformed();
  }, [idle]);

  return state === "performed";
}
