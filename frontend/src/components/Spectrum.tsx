// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect, useRef } from "react";
import { cn } from "@/lib/cn";
import { SPECTRUM_BANDS, visualizer } from "@/lib/audioVisualizer";
import { useVisualizerEnabled } from "@/lib/useVisualizerEnabled";

/**
 * Спектр звука. Значения пишутся прямо в стили столбиков через ref: подписывать на них
 * React означало бы 60 рендеров в секунду с перерисовкой всего плеера. Тот же приём уже
 * используется в отсчёте перед строкой текста песни (`LyricsIntro`).
 *
 * Когда спектра нет — выключен, `prefers-reduced-motion`, отвод не отдал звук — столбики
 * остаются на месте и просто не двигаются; отдельная заглушка не нужна.
 */
export function Spectrum({
  className,
  bars = 28,
  mirrored = true,
}: {
  className?: string;
  bars?: number;
  /**
   * Зеркально, как в cava: бас по краям, верх сходится к середине. Так картинка
   * симметрична относительно центрированного транспорта, а под самими кнопками
   * оказывается тихая верхняя часть спектра, а не постоянно бьющий бас.
   */
  mirrored?: boolean;
}) {
  const enabled = useVisualizerEnabled();
  const container = useRef<HTMLSpanElement>(null);

  useEffect(() => {
    if (!enabled) return;

    const element = container.current;
    if (!element) return;

    const columns = [...element.children] as HTMLElement[];

    // Сколько столбиков приходится на одну сторону: в зеркальном режиме половина.
    const side = mirrored ? Math.ceil(columns.length / 2) : columns.length;

    // Номер полосы для столбика: у края — нулевая (бас), к середине — верх спектра.
    const bandOf = (index: number) => {
      const position = mirrored ? Math.min(index, columns.length - 1 - index) : index;
      return Math.min(SPECTRUM_BANDS - 1, Math.floor((position / side) * SPECTRUM_BANDS));
    };

    const bands = columns.map((_, index) => bandOf(index));

    const unsubscribe = visualizer.subscribe((levels) => {
      for (let index = 0; index < columns.length; index += 1) {
        const level = levels[bands[index]] ?? 0;
        columns[index].style.transform = `scaleY(${Math.max(0.03, level).toFixed(3)})`;
      }
    });

    return () => {
      unsubscribe();
      for (const column of columns) column.style.transform = "";
    };
  }, [enabled, mirrored]);

  if (!enabled) return null;

  return (
    <span
      ref={container}
      aria-hidden="true"
      // `gap-px` на полусотне столбиков браузер округляет в ноль, и они слипаются в плиту.
      className={cn("pointer-events-none flex items-end gap-0.5", className)}
    >
      {Array.from({ length: bars }, (_, index) => (
        // Высота покоя задана классом, а не инлайном: снятый инлайновый transform
        // означает scaleY(1), то есть столбики в полный рост — именно так спектр и
        // выглядел при `prefers-reduced-motion`, когда кадры не приходят вовсе.
        //
        // Именно `[transform:…]`, а не утилита `scale-y-*`: в Tailwind v4 та пишет
        // отдельное свойство `scale`, которое перемножилось бы с инлайновым `transform`
        // от JS — и столбики получались бы в тридцать раз ниже, чем нужно.
        <span
          key={index}
          className="h-full w-full origin-bottom rounded-t-[1px] bg-current [transform:scaleY(0.03)]"
        />
      ))}
    </span>
  );
}
