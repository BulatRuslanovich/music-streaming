"use client";

import { useEffect } from "react";

/**
 * Отдаёт цвет обложки играющего трека всему интерфейсу: из --cover-tint выводится --primary,
 * а значит фирменный цвет кнопок, прогресса и активной навигации едет за музыкой.
 *
 * Переменная ставится на корне, а не на плеере, именно поэтому: покрасить нужно всё,
 * а не одну панель внизу. Когда цвет снять не удалось, свойство снимается — и в силу
 * вступает запасное значение из @property, зелёное.
 */
export function useCoverAccent(tint: string | null): void {
  useEffect(() => {
    const root = document.documentElement;

    if (tint) root.style.setProperty("--cover-tint", tint);
    else root.style.removeProperty("--cover-tint");
  }, [tint]);
}
