// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect, useRef } from "react";

/**
 * Глобальные клавиши без перерегистрации слушателя. Обработчику нужны свежие замыкания,
 * и раньше это делалось эффектом вовсе без зависимостей — то есть add/removeEventListener
 * на каждый рендер, а плеер рендерится по тику прогресса. Ссылка на последний обработчик
 * решает то же самое одной подпиской.
 */
export function useWindowKeyDown(handler: (event: KeyboardEvent) => void): void {
  const latest = useRef(handler);

  useEffect(() => {
    latest.current = handler;
  }, [handler]);

  useEffect(() => {
    const listener = (event: KeyboardEvent) => latest.current(event);

    window.addEventListener("keydown", listener);
    return () => window.removeEventListener("keydown", listener);
  }, []);
}
