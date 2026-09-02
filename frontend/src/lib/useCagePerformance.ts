// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect, useSyncExternalStore } from "react";
import {
  CAGE_MS,
  cagePerformed,
  markCagePerformed,
  serverCagePerformed,
  subscribeCage,
} from "./silence";
import { useIdle } from "./useIdle";

/**
 * Пауза плюс 4′33″ полного бездействия — это уже исполнение пьесы, а не простой.
 *
 * Вызывать это нужно там, где компонент смонтирован всегда: отсчёт, живший внутри
 * полноэкранного плеера, не заводился в единственном сценарии, ради которого всё и затевалось —
 * поставить на паузу и отойти, не открывая арт.
 *
 * `useIdle` сам отключается на грубом указателе, так что находка остаётся десктопной.
 */
export function useCagePerformance(paused: boolean): boolean {
  const performed = useSyncExternalStore(subscribeCage, cagePerformed, serverCagePerformed);
  const idle = useIdle(CAGE_MS, paused && !performed);

  useEffect(() => {
    if (idle) markCagePerformed();
  }, [idle]);

  return performed;
}
