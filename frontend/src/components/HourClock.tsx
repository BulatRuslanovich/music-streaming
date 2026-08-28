// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useState } from "react";
import { cn } from "@/lib/cn";
import type { HourlyActivity } from "@/lib/types";
import { useT } from "@/contexts/I18nContext";
import { ActivityTable } from "./ActivityTable";

/**
 * Сутки — это круг, а не отрезок: полночь соседствует с 23:00, и на столбчатом графике
 * эта пара оказывалась на противоположных краях. Плюс два одинаковых бар-чарта подряд
 * читались как один и тот же график, показанный дважды.
 *
 * Дырка в середине не украшение: в ней живут цифры наведённого часа, поэтому подписи
 * не нужно нигде позиционировать.
 */

const SIZE = 200;

const CENTER = SIZE / 2;

/** Отверстие держит цифры наведённого часа, поэтому меряется по ним, а не на глаз. */
const INNER_RADIUS = 50;

const OUTER_RADIUS = 82;

/** Подписи часов живут внутри того же viewBox — снаружи их просто срезало бы. */
const LABEL_RADIUS = 93;

const HOURS = 24;

const HOUR_ANGLE = 360 / HOURS;

/** Даже пустой час остаётся видимым сегментом — иначе в круге появляются дыры. */
const MIN_REACH = 0.06;

export function HourClock({
  hours,
  columnLabel,
  tableLabel,
  formatValue,
}: {
  hours: HourlyActivity[];
  columnLabel: string;
  tableLabel: string;
  formatValue: (seconds: number) => string;
}) {
  const t = useT();
  const [hovered, setHovered] = useState<number | null>(null);

  const byHour = new Map(hours.map((entry) => [entry.hour, entry]));

  const points = Array.from({ length: HOURS }, (_, hour) => {
    const entry = byHour.get(hour);

    return {
      key: labelFor(hour),
      label: labelFor(hour),
      value: entry?.listenedSeconds ?? 0,
      plays: entry?.plays ?? 0,
      hour,
    };
  });

  const max = Math.max(...points.map((point) => point.value));

  const peak = points.reduce((best, point) => (point.value > best.value ? point : best), points[0]);
  const shown = hovered !== null ? points[hovered] : peak;
  const hasAny = max > 0;

  return (
    <figure className="m-0 flex flex-col items-center">
      <div className="relative" onPointerLeave={() => setHovered(null)}>
        <svg viewBox={`0 0 ${SIZE} ${SIZE}`} aria-hidden="true" className="size-72 max-md:size-60">
          {points.map((point) => {
            const reach = hasAny ? Math.max(MIN_REACH, point.value / max) : MIN_REACH;
            const outer = INNER_RADIUS + (OUTER_RADIUS - INNER_RADIUS) * reach;
            const start = point.hour * HOUR_ANGLE;

            return (
              <path
                key={point.hour}
                d={wedge(INNER_RADIUS, outer, start + 1, start + HOUR_ANGLE - 1)}
                onPointerEnter={() => setHovered(point.hour)}
                className={cn(
                  "transition-opacity duration-150 ease-brand",
                  point.value > 0 ? "fill-primary" : "fill-raised",
                  hovered !== null && hovered !== point.hour ? "opacity-40" : "opacity-90",
                )}
              />
            );
          })}

          {[0, 6, 12, 18].map((hour) => {
            const { x, y } = pointOn(LABEL_RADIUS, hour * HOUR_ANGLE + HOUR_ANGLE / 2);

            return (
              <text
                key={hour}
                x={x}
                y={y}
                textAnchor="middle"
                dominantBaseline="central"
                className="fill-[var(--faint-foreground)] text-[9px] tabular-nums"
              >
                {String(hour).padStart(2, "0")}
              </text>
            );
          })}
        </svg>

        <div className="pointer-events-none absolute inset-0 grid place-items-center text-center">
          {/* Ширина ограничена диаметром отверстия: длинная подпись иначе уезжает под кольцо. */}
          <span className="flex w-[45%] flex-col leading-tight">
            {hasAny ? (
              <>
                <span className="text-lg font-bold tabular-nums">{labelFor(shown.hour)}</span>
                <span className="text-2xs text-muted-foreground tabular-nums">
                  {formatValue(shown.value)}
                </span>
                <span className="truncate text-2xs text-faint">
                  {t("stats.playCount", { count: shown.plays })}
                </span>
              </>
            ) : (
              <span className="text-2xs text-faint">{t("stats.empty")}</span>
            )}
          </span>
        </div>
      </div>

      <ActivityTable
        points={points}
        columnLabel={columnLabel}
        tableLabel={tableLabel}
        formatValue={formatValue}
      />
    </figure>
  );
}

function labelFor(hour: number): string {
  return `${String(hour).padStart(2, "0")}:00`;
}

/** Полночь наверху, дальше по часовой стрелке — как на циферблате. */
function pointOn(radius: number, degrees: number): { x: number; y: number } {
  const radians = ((degrees - 90) * Math.PI) / 180;

  return {
    x: CENTER + radius * Math.cos(radians),
    y: CENTER + radius * Math.sin(radians),
  };
}

function wedge(inner: number, outer: number, from: number, to: number): string {
  const outerFrom = pointOn(outer, from);
  const outerTo = pointOn(outer, to);
  const innerTo = pointOn(inner, to);
  const innerFrom = pointOn(inner, from);

  // Сектор всегда меньше полуокружности, поэтому large-arc везде 0.
  return [
    `M ${outerFrom.x} ${outerFrom.y}`,
    `A ${outer} ${outer} 0 0 1 ${outerTo.x} ${outerTo.y}`,
    `L ${innerTo.x} ${innerTo.y}`,
    `A ${inner} ${inner} 0 0 0 ${innerFrom.x} ${innerFrom.y}`,
    "Z",
  ].join(" ");
}
