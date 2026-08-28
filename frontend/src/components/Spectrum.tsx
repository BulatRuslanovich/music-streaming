// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect, useRef, useState, type CSSProperties } from "react";
import { cn } from "@/lib/cn";
import { visualizer } from "@/lib/audioVisualizer";
import { bandPositions, barCount, sampleAt } from "@/lib/spectrumLayout";
import { useVisualizerEnabled } from "@/lib/useVisualizerEnabled";

const REST_LEVEL = 0.1;

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
  mirrored = true,
}: {
  className?: string;
  /**
   * Зеркально, как в cava: бас по краям, верх сходится к середине. Так картинка
   * симметрична относительно центрированного транспорта, а под самими кнопками
   * оказывается тихая верхняя часть спектра, а не постоянно бьющий бас.
   */
  mirrored?: boolean;
}) {
  const enabled = useVisualizerEnabled();
  const container = useRef<HTMLSpanElement>(null);
  const [bars, setBars] = useState(0);

  useEffect(() => {
    if (!enabled) return;

    const element = container.current;
    if (!element) return;

    const measure = (width: number) => setBars(barCount(width));

    // CSS уже решает, виден ли спектр. Нулевая ширина скрытого элемента означает ноль
    // столбиков, ноль подписчиков и, следовательно, ни AudioContext, ни rAF на телефоне.
    measure(element.clientWidth);

    if (typeof ResizeObserver === "undefined") return;

    const observer = new ResizeObserver(([entry]) => measure(entry.contentRect.width));
    observer.observe(element);

    return () => observer.disconnect();
  }, [enabled]);

  useEffect(() => {
    if (!enabled || bars === 0) return;

    const element = container.current;
    if (!element) return;
    // Состояние могло сохранить число столбиков после выключения настройки на десктопе.
    // Не подписываемся даже на один кадр, если за это время элемент стал скрытым.
    if (element.clientWidth <= 0) return;

    const columns = [...element.children] as HTMLElement[];
    const positions = bandPositions(columns.length, mirrored);
    const lastTransforms = Array<string>(columns.length).fill("");

    const unsubscribe = visualizer.subscribe((levels) => {
      for (let index = 0; index < columns.length; index += 1) {
        const level = Math.max(REST_LEVEL, sampleAt(levels, positions[index]));
        const transform = `translateY(${((1 - level) * 100).toFixed(1)}%)`;

        if (lastTransforms[index] === transform) continue;
        columns[index].style.transform = transform;
        lastTransforms[index] = transform;
      }
    });

    return () => {
      unsubscribe();
      for (const column of columns) column.style.transform = "";
    };
  }, [bars, enabled, mirrored]);

  if (!enabled) return null;

  return (
    <span
      ref={container}
      aria-hidden="true"
      style={{ "--spectrum-span": `${bars * 100}%` } as CSSProperties}
      // Ширина столбика целая и фиксированная: `flex-1` делил остаток на дробные пиксели,
      // и Chromium растрировал соседние столбики то в 5, то в 6px. Остаток отдаём зазорам.
      className={cn(
        "spectrum pointer-events-none flex justify-between overflow-hidden [contain:layout_paint]",
        className,
      )}
    >
      {Array.from({ length: bars }, (_, index) => (
        // Столбик всегда полной высоты, а контейнер обрезает уведённую вниз часть.
        // В отличие от scaleY, translateY не сплющивает круглую шапку вместе с высотой.
        //
        // Зелёный background-color остаётся под прозрачным градиентом до загрузки обложки.
        <span
          key={index}
          style={{ backgroundPositionX: `${(index / Math.max(1, bars - 1)) * 100}%` }}
          className={cn(
            "h-full w-[5px] shrink-0 rounded-full bg-primary/45",
            "[background-image:linear-gradient(90deg,var(--spectrum-a),var(--spectrum-b))]",
            "[background-size:var(--spectrum-span)_100%] [transform:translateY(90%)]",
          )}
        />
      ))}
    </span>
  );
}
